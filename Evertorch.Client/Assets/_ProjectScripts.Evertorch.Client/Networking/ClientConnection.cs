using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     Runs the client side of the protocol over a transport: hello, character selection, world entry, and then the
///     routing of world messages into a <see cref="ClientWorld" />. It trusts nothing it receives beyond what decodes
///     cleanly.
/// </summary>
public sealed class ClientConnection : IClientTransportListener, IMoveIntentSink, ICombatCommandSink,
    IPickupCommandSink, ISkillCommandSink
{
    private readonly IClientTransport m_transport;
    private readonly ClientConnectionSettings m_settings;
    private readonly IMapProvider m_maps;
    private readonly byte[] m_sendBuffer = new byte[ProtocolLimits.MaxClientPayloadBytes];
    private uint m_commandSequence;

    public ClientConnection(IClientTransport transport, ClientConnectionSettings settings, IMapProvider maps)
    {
        m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_maps = maps ?? throw new ArgumentNullException(nameof(maps));
    }

    public ClientConnectionState State { get; private set; }

    public ClientWorld? World { get; private set; }

    /// <summary>
    ///     The account's characters as the server last listed them; empty until the first list.
    /// </summary>
    public IReadOnlyList<CharacterListEntry> Characters { get; private set; } = Array.Empty<CharacterListEntry>();

    /// <summary>
    ///     The answer to the last <see cref="CreateCharacter" />, or <see cref="CreateCharacterOutcome.None" />. Entering
    ///     the world clears it, so the list after a logout is not taken for its answer.
    /// </summary>
    public CreateCharacterOutcome LastCreateOutcome { get; private set; }

    public uint ServerTickRate { get; private set; }

    public string ServerBuildVersion { get; private set; } = string.Empty;

    public TransportDisconnectCause DisconnectCause { get; private set; }

    public DisconnectNotice? Notice { get; private set; }

    /// <summary>
    ///     Why the client itself gave up, when it did. Never contains the session token.
    /// </summary>
    public string LocalError { get; private set; } = string.Empty;

    public int MalformedMessages { get; private set; }

    public int UnexpectedMessages { get; private set; }

    public int RoundTripMilliseconds => m_transport.RoundTripMilliseconds;

    /// <summary>
    ///     Characters can be created or entered: on the list, or waiting for an entry the server may never answer.
    /// </summary>
    public bool IsOnCharacterList =>
        State == ClientConnectionState.SelectingCharacter || State == ClientConnectionState.EnteringWorld;

    void IClientTransportListener.OnConnected()
    {
        if (State != ClientConnectionState.Connecting)
        {
            return;
        }

        ContentVersionCodec.TryToWire(m_settings.ContentVersion, out uint contentVersion);
        var hello = new ClientHello(
            ProtocolConstants.ProtocolVersion,
            m_settings.BuildVersion,
            contentVersion,
            m_settings.SessionToken);
        if (hello.GetEncodedLength() > m_sendBuffer.Length)
        {
            Fail("The hello does not fit the client payload limit.");
            m_transport.Disconnect();
            return;
        }

        State = ClientConnectionState.AwaitingHello;
        SendRouted(MessageOpcode.ClientHello, hello.Write(m_sendBuffer));
    }

    void IClientTransportListener.OnDisconnected(TransportDisconnectCause cause, ReadOnlySpan<byte> notice)
    {
        if (State == ClientConnectionState.Disconnected)
        {
            return;
        }

        if (!notice.IsEmpty && DisconnectNotice.TryRead(notice, out DisconnectNotice? decoded))
        {
            Notice = decoded;
        }

        DisconnectCause = cause;
        Close();
    }

    void IClientTransportListener.OnPayload(ProtocolChannel channel, ReadOnlySpan<byte> payload)
    {
        if (!MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode)
            || MessageRouting.IsClientToServer(opcode)
            || !MessageRouting.TryGetRoute(opcode, out ProtocolChannel expected, out MessageDelivery _)
            || expected != channel)
        {
            MalformedMessages++;
            return;
        }

        switch (opcode)
        {
            case MessageOpcode.ServerHello:
                OnServerHello(payload);
                break;
            case MessageOpcode.WorldEntered:
                OnWorldEntered(payload);
                break;
            case MessageOpcode.CharacterList:
                OnCharacterList(payload);
                break;
            case MessageOpcode.CreateCharacterResult:
                OnCreateCharacterResult(payload);
                break;
            case MessageOpcode.LogoutComplete:
                OnLogoutComplete(payload);
                break;
            case MessageOpcode.EntitySpawn:
                OnEntitySpawn(payload);
                break;
            case MessageOpcode.EntityDespawn:
                OnEntityDespawn(payload);
                break;
            case MessageOpcode.EntitySnapshot:
                OnEntitySnapshot(payload);
                break;
            case MessageOpcode.TargetChanged:
                OnTargetChanged(payload);
                break;
            case MessageOpcode.AttackStarted:
                if (AttackStarted.TryRead(payload, out AttackStarted started))
                {
                    WithWorld(world => world.OnAttackStarted(started));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.Damage:
                if (Damage.TryRead(payload, out Damage damage))
                {
                    WithWorld(world => world.OnDamage(damage));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.SkillCastStarted:
                if (SkillCastStarted.TryRead(payload, out SkillCastStarted? castStarted) && castStarted != null)
                {
                    WithWorld(world => world.OnSkillCastStarted(castStarted));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.SkillResolved:
                if (SkillResolved.TryRead(payload, out SkillResolved? resolved) && resolved != null)
                {
                    WithWorld(world => world.OnSkillResolved(resolved));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.SkillList:
                if (SkillList.TryRead(payload, out SkillList? skills) && skills != null)
                {
                    WithWorld(world => world.OnSkillList(skills));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.StatusEffects:
                if (StatusEffects.TryRead(payload, out StatusEffects? effects) && effects != null)
                {
                    WithWorld(world => world.OnStatusEffects(effects));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.EntityDied:
                if (EntityDied.TryRead(payload, out EntityDied died))
                {
                    WithWorld(world => world.OnEntityDied(died));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.CharacterHealth:
                if (CharacterHealth.TryRead(payload, out CharacterHealth health))
                {
                    WithWorld(world => world.OnCharacterHealth(health));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.CharacterProgress:
                if (CharacterProgress.TryRead(payload, out CharacterProgress progress))
                {
                    WithWorld(world => world.OnCharacterProgress(progress));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.EntityRevived:
                if (EntityRevived.TryRead(payload, out EntityRevived revived))
                {
                    WithWorld(world => world.OnEntityRevived(revived));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.ItemDropped:
                if (ItemDropped.TryRead(payload, out ItemDropped? dropped) && dropped != null)
                {
                    WithWorld(world => world.OnItemDropped(dropped));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.ItemPickedUp:
                if (ItemPickedUp.TryRead(payload, out ItemPickedUp? pickedUp) && pickedUp != null)
                {
                    WithWorld(world => world.OnItemPickedUp(pickedUp));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.CommandRejected:
                if (CommandRejected.TryRead(payload, out CommandRejected rejected))
                {
                    WithWorld(world => world.OnCommandRejected(rejected));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.InventorySnapshot:
                if (InventorySnapshot.TryRead(payload, out InventorySnapshot? part) && part != null)
                {
                    WithWorld(world => ResyncIf(world.Inventory.OnSnapshot(part)));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.InventoryChanged:
                if (InventoryChanged.TryRead(payload, out InventoryChanged? change) && change != null)
                {
                    WithWorld(world => ResyncIf(world.Inventory.OnChanged(change)));
                }
                else
                {
                    MalformedMessages++;
                }

                break;
            case MessageOpcode.DisconnectNotice:
                OnDisconnectNotice(payload);
                break;
            default:
                UnexpectedMessages++;
                break;
        }
    }

    /// <summary>
    ///     Asks the server to attack <paramref name="target" /> repeatedly. Commands share one sequence per connection,
    ///     starting at 1, which the server uses to drop duplicates.
    /// </summary>
    public uint SendAttack(EntityId target)
    {
        if (State != ClientConnectionState.InWorld)
        {
            return 0;
        }

        uint sequence = NextCommandSequence();
        new AttackEntity(target, sequence).Write(m_sendBuffer);
        SendRouted(MessageOpcode.AttackEntity, AttackEntity.EncodedLength);
        return sequence;
    }

    public void SendCancel()
    {
        if (State != ClientConnectionState.InWorld)
        {
            return;
        }

        new CancelAction(NextCommandSequence()).Write(m_sendBuffer);
        SendRouted(MessageOpcode.CancelAction, CancelAction.EncodedLength);
        World?.OnLocalCancel();
    }

    public void Send(MoveIntent intent)
    {
        if (State != ClientConnectionState.InWorld)
        {
            return;
        }

        if (intent.DirectionX == 0f && intent.DirectionZ == 0f)
        {
            int length = new StopMovement(intent.Sequence, intent.ClientTick).Write(m_sendBuffer);
            SendRouted(MessageOpcode.StopMovement, length);
        }
        else
        {
            int length = new MoveInput(intent).Write(m_sendBuffer);
            SendRouted(MessageOpcode.MoveInput, length);
        }
    }

    /// <summary>
    ///     Asks the server to pick up <paramref name="drop" />; it shares the command sequence with attacks.
    /// </summary>
    public uint SendPickup(EntityId drop)
    {
        if (State != ClientConnectionState.InWorld)
        {
            return 0;
        }

        uint sequence = NextCommandSequence();
        new PickupItem(drop, sequence).Write(m_sendBuffer);
        SendRouted(MessageOpcode.PickupItem, PickupItem.EncodedLength);
        return sequence;
    }

    /// <summary>
    ///     Asks the server to cast <paramref name="skill" /> at <paramref name="target" />, the default value for the
    ///     local character itself. Returns the command's sequence, or 0 outside the world.
    /// </summary>
    public uint SendUseSkill(SkillDefinitionId skill, EntityId target)
    {
        if (State != ClientConnectionState.InWorld)
        {
            return 0;
        }

        uint sequence = NextCommandSequence();
        var message = new UseSkill(skill, target, sequence);
        SendRouted(MessageOpcode.UseSkill, message.Write(m_sendBuffer));
        return sequence;
    }

    /// <summary>
    ///     Asks the server to select <paramref name="target" />, or to clear the selection for entity 0. The world
    ///     shows a target only once the server confirms it.
    /// </summary>
    public void SendTarget(EntityId target)
    {
        if (State != ClientConnectionState.InWorld)
        {
            return;
        }

        new TargetEntity(target).Write(m_sendBuffer);
        SendRouted(MessageOpcode.TargetEntity, TargetEntity.EncodedLength);
    }

    public void SendRespawn()
    {
        if (State != ClientConnectionState.InWorld)
        {
            return;
        }

        new Respawn(NextCommandSequence()).Write(m_sendBuffer);
        SendRouted(MessageOpcode.Respawn, Respawn.EncodedLength);
    }

    /// <summary>
    ///     Asks to leave the world for character selection. The world stays until the server confirms with
    ///     <c>LogoutComplete</c>, which it sends only after the character's checkpoint is written.
    /// </summary>
    public void SendLogout()
    {
        if (State != ClientConnectionState.InWorld)
        {
            return;
        }

        new Logout(NextCommandSequence()).Write(m_sendBuffer);
        SendRouted(MessageOpcode.Logout, Logout.EncodedLength);
    }

    /// <summary>
    ///     Asks for a new character on the account. False, sending nothing, unless characters are being selected and
    ///     the name fits the protocol; the server applies the naming policy and answers with
    ///     <see cref="LastCreateOutcome" /> and a new list.
    /// </summary>
    public bool CreateCharacter(string name)
    {
        var message = new CreateCharacter(name);
        if (!IsOnCharacterList
            || Encoding.UTF8.GetByteCount(name) > ProtocolLimits.MaxCharacterNameBytes)
        {
            return false;
        }

        LastCreateOutcome = CreateCharacterOutcome.None;
        SendRouted(MessageOpcode.CreateCharacter, message.Write(m_sendBuffer));
        return true;
    }

    /// <summary>
    ///     Asks to enter the world as <paramref name="character" />. False, sending nothing, unless characters are
    ///     being selected. A refused entry gets no reply (Network Protocol §4), so while an earlier request is
    ///     unanswered another may be sent: the client stays on the list.
    /// </summary>
    public bool EnterWorld(CharacterId character)
    {
        if (!IsOnCharacterList)
        {
            return false;
        }

        State = ClientConnectionState.EnteringWorld;
        SendRouted(MessageOpcode.EnterWorldRequest, new EnterWorldRequest(character).Write(m_sendBuffer));
        return true;
    }

    /// <summary>
    ///     Raised after every character list, including the one that follows a creation.
    /// </summary>
    public event Action? CharactersChanged;

    public event Action<ClientWorld>? EnteredWorld;

    /// <summary>
    ///     Raised when a logout completes: the world is gone and characters are being selected again.
    /// </summary>
    public event Action? LeftWorld;

    public event Action? Closed;

    public void Connect(string host, int port)
    {
        if (State != ClientConnectionState.Disconnected)
        {
            throw new InvalidOperationException("The connection is already in use.");
        }

        World = null;
        Characters = Array.Empty<CharacterListEntry>();
        LastCreateOutcome = CreateCharacterOutcome.None;
        Notice = null;
        LocalError = string.Empty;
        DisconnectCause = TransportDisconnectCause.None;
        if (!ContentVersionCodec.TryToWire(m_settings.ContentVersion, out uint _))
        {
            Fail("The client content version is not a valid content version.");
            return;
        }

        State = ClientConnectionState.Connecting;
        try
        {
            m_transport.Connect(host, port);
        }
        catch (InvalidOperationException exception)
        {
            Fail($"The connection could not be started: {exception.Message}");
        }
        catch (SocketException exception)
        {
            Fail($"The connection could not be started: {exception.Message}");
        }
    }

    public void Disconnect()
    {
        if (State != ClientConnectionState.Disconnected)
        {
            m_transport.Disconnect();
        }
    }

    public void Poll()
    {
        m_transport.Poll(this);
    }

    private void OnServerHello(ReadOnlySpan<byte> payload)
    {
        if (!ServerHello.TryRead(payload, out ServerHello? hello) || hello == null || hello.ServerTickRate == 0)
        {
            MalformedMessages++;
            return;
        }

        if (State != ClientConnectionState.AwaitingHello)
        {
            UnexpectedMessages++;
            return;
        }

        ServerTickRate = hello.ServerTickRate;
        ServerBuildVersion = hello.ServerBuildVersion;
        State = ClientConnectionState.SelectingCharacter;
    }

    private void OnCharacterList(ReadOnlySpan<byte> payload)
    {
        if (!CharacterList.TryRead(payload, out CharacterList? list) || list == null)
        {
            MalformedMessages++;
            return;
        }

        // A list can still arrive after an enter request crossed it on the way.
        if (State != ClientConnectionState.SelectingCharacter && State != ClientConnectionState.EnteringWorld)
        {
            UnexpectedMessages++;
            return;
        }

        Characters = list.Characters;
        CharactersChanged?.Invoke();
    }

    private void OnLogoutComplete(ReadOnlySpan<byte> payload)
    {
        if (!LogoutComplete.TryRead(payload, out LogoutComplete _))
        {
            MalformedMessages++;
            return;
        }

        if (State != ClientConnectionState.InWorld)
        {
            UnexpectedMessages++;
            return;
        }

        World = null;
        State = ClientConnectionState.SelectingCharacter;
        LeftWorld?.Invoke();
    }

    private void OnCreateCharacterResult(ReadOnlySpan<byte> payload)
    {
        if (!CreateCharacterResult.TryRead(payload, out CreateCharacterResult result))
        {
            MalformedMessages++;
            return;
        }

        if (State != ClientConnectionState.SelectingCharacter && State != ClientConnectionState.EnteringWorld)
        {
            UnexpectedMessages++;
            return;
        }

        LastCreateOutcome = result.Outcome;
    }

    private void OnWorldEntered(ReadOnlySpan<byte> payload)
    {
        if (!WorldEntered.TryRead(payload, out WorldEntered? entered) || entered == null)
        {
            MalformedMessages++;
            return;
        }

        if (State != ClientConnectionState.EnteringWorld)
        {
            UnexpectedMessages++;
            return;
        }

        if (!m_maps.TryGetNavigation(entered.Map, out NavigationGrid? grid) || grid == null)
        {
            Fail($"The client content has no map '{entered.Map.Value}'.");
            m_transport.Disconnect();
            return;
        }

        World = new ClientWorld(grid, entered, ServerTickRate);
        LastCreateOutcome = CreateCharacterOutcome.None;

        // The command sequence belongs to the character, not the connection: after a reconnect it goes on from the
        // newest the server processed, or every command would look like a replay (Network Protocol §8).
        m_commandSequence = entered.LastCommandSequence;
        State = ClientConnectionState.InWorld;
        EnteredWorld?.Invoke(World);
    }

    private void OnEntitySpawn(ReadOnlySpan<byte> payload)
    {
        if (!EntitySpawn.TryRead(payload, out EntitySpawn? spawn) || spawn == null)
        {
            MalformedMessages++;
        }
        else if (World == null)
        {
            UnexpectedMessages++;
        }
        else
        {
            World.OnSpawn(spawn);
        }
    }

    private void OnEntityDespawn(ReadOnlySpan<byte> payload)
    {
        if (!EntityDespawn.TryRead(payload, out EntityDespawn despawn))
        {
            MalformedMessages++;
        }
        else if (World == null)
        {
            UnexpectedMessages++;
        }
        else
        {
            World.OnDespawn(despawn);
        }
    }

    private void WithWorld(Action<ClientWorld> handle)
    {
        if (World == null)
        {
            UnexpectedMessages++;
        }
        else
        {
            handle(World);
        }
    }

    private void OnTargetChanged(ReadOnlySpan<byte> payload)
    {
        if (!TargetChanged.TryRead(payload, out TargetChanged changed))
        {
            MalformedMessages++;
        }
        else if (World == null)
        {
            UnexpectedMessages++;
        }
        else
        {
            World.OnTargetChanged(changed);
        }
    }

    private void OnEntitySnapshot(ReadOnlySpan<byte> payload)
    {
        if (!EntitySnapshot.TryRead(payload, out EntitySnapshot? snapshot) || snapshot == null)
        {
            MalformedMessages++;
        }
        else if (World == null)
        {
            // Snapshots are unreliable and can overtake the reliable world entry.
            UnexpectedMessages++;
        }
        else
        {
            World.OnSnapshot(snapshot);
        }
    }

    private void OnDisconnectNotice(ReadOnlySpan<byte> payload)
    {
        if (DisconnectNotice.TryRead(payload, out DisconnectNotice? notice))
        {
            Notice = notice;
        }
        else
        {
            MalformedMessages++;
        }
    }

    private void ResyncIf(bool isNeeded)
    {
        if (isNeeded && State == ClientConnectionState.InWorld)
        {
            SendRouted(MessageOpcode.InventoryResyncRequest, new InventoryResyncRequest().Write(m_sendBuffer));
        }
    }

    private uint NextCommandSequence()
    {
        m_commandSequence++;
        return m_commandSequence;
    }

    private void SendRouted(MessageOpcode opcode, int length)
    {
        MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery);
        m_transport.Send(channel, delivery, new ReadOnlySpan<byte>(m_sendBuffer, 0, length));
    }

    private void Fail(string error)
    {
        LocalError = error;
        if (State == ClientConnectionState.Disconnected)
        {
            Closed?.Invoke();
        }
        else
        {
            Close();
        }
    }

    private void Close()
    {
        if (State == ClientConnectionState.Disconnected)
        {
            return;
        }

        // A closed connection has no world. Anything still holding the old one must not mistake it for live.
        State = ClientConnectionState.Disconnected;
        World = null;
        Closed?.Invoke();
    }
}
}
