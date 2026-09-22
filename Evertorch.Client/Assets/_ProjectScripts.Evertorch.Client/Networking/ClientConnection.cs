using System;
using System.Net.Sockets;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     Runs the client side of the protocol over a transport: hello, world entry, and then the routing of world
///     messages into a <see cref="ClientWorld" />. It trusts nothing it receives beyond what decodes cleanly.
/// </summary>
public sealed class ClientConnection : IClientTransportListener, IMoveIntentSink
{
    private readonly IClientTransport m_transport;
    private readonly ClientConnectionSettings m_settings;
    private readonly IMapProvider m_maps;
    private readonly byte[] m_sendBuffer = new byte[ProtocolLimits.MaxClientPayloadBytes];

    public ClientConnection(IClientTransport transport, ClientConnectionSettings settings, IMapProvider maps)
    {
        m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_maps = maps ?? throw new ArgumentNullException(nameof(maps));
    }

    public ClientConnectionState State { get; private set; }

    public ClientWorld? World { get; private set; }

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
            case MessageOpcode.EntitySpawn:
                OnEntitySpawn(payload);
                break;
            case MessageOpcode.EntityDespawn:
                OnEntityDespawn(payload);
                break;
            case MessageOpcode.EntitySnapshot:
                OnEntitySnapshot(payload);
                break;
            case MessageOpcode.DisconnectNotice:
                OnDisconnectNotice(payload);
                break;
            default:
                UnexpectedMessages++;
                break;
        }
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

    public event Action<ClientWorld>? EnteredWorld;

    public event Action? Closed;

    public void Connect(string host, int port)
    {
        if (State != ClientConnectionState.Disconnected)
        {
            throw new InvalidOperationException("The connection is already in use.");
        }

        World = null;
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
        State = ClientConnectionState.EnteringWorld;
        int length = new EnterWorldRequest(m_settings.Character).Write(m_sendBuffer);
        SendRouted(MessageOpcode.EnterWorldRequest, length);
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
