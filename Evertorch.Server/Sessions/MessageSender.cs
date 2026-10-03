using System;
using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Encodes server messages into one reusable buffer. Tick thread only, which is what makes the shared buffer safe.
/// </summary>
public sealed class MessageSender
{
    // Larger than any message sent today; snapshots are sized to fit one unreliable datagram well below this.
    private const int BufferBytes = 2048;

    private readonly IOutboundMessages m_outbound;
    private readonly byte[] m_buffer = new byte[BufferBytes];

    public MessageSender(IOutboundMessages outbound)
    {
        m_outbound = outbound;
    }

    public void Send(ConnectionId connection, ServerHello message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, WorldEntered message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntitySpawn message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, ChatReceived message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, PartyRoster message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, PartyMemberStatus message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, BossAnnouncement message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    /// <summary>
    ///     A boss's award to its most valuable player alone, while a connection controls it in the world (Network
    ///     Protocol §9); a player away hears nothing of it.
    /// </summary>
    public void SendMvpAwarded(CharacterSession character, MvpAwarded message)
    {
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld)
        {
            m_outbound.Send(owner.Connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
        }
    }

    public void Send(ConnectionId connection, PartyEvent message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, WornWeaponChanged message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, NpcServices message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntityDespawn message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntitySnapshot message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, TargetChanged message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, AttackStarted message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, Damage message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntityDied message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    /// <summary>
    ///     <paramref name="player" />'s exact HP and SP to its owner alone (Network Protocol §9); nothing while no
    ///     connection controls it.
    /// </summary>
    public void SendHealth(PlayerEntity player)
    {
        if (player.Owner != default)
        {
            Send(
                player.Owner,
                new CharacterHealth(
                    (uint)player.CurrentHealth,
                    (uint)player.MaxHealth,
                    (uint)player.CurrentSpirit,
                    (uint)player.MaxSpirit));
        }
    }

    public void Send(ConnectionId connection, SkillCastStarted message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, SkillResolved message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, SkillList message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, QuestLog message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, StatusEffects message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CharacterSheet message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CharacterProgress message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CharacterHealth message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntityRevived message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CharacterList message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CreateCharacterResult message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CommandRejected message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, LogoutComplete message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, InventorySnapshot message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    /// <summary>
    ///     A committed change of <paramref name="character" />'s inventory to its owner alone (Network Protocol §9): the
    ///     rows it changed, from <paramref name="priorRevision" /> to the inventory's revision now, and its coins; a
    ///     change that moved only coins carries no row. Nothing goes to a character no connection controls.
    /// </summary>
    public void SendInventoryChange(CharacterSession character, uint priorRevision, IReadOnlyList<InventoryEntry> rows)
    {
        ClientSession? owner = character.Connection;
        if (owner == null)
        {
            return;
        }

        // A change in which only the coins moved carries no row (Network Protocol §9).
        if (owner.State == SessionState.InWorld)
        {
            Send(
                owner.Connection,
                new InventoryChanged(
                    priorRevision,
                    character.Inventory.Revision,
                    (uint)character.Inventory.Coins,
                    rows));
        }
    }

    public void Send(ConnectionId connection, InventoryChanged message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, ItemPickedUp message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, ItemDropped message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Disconnect(ConnectionId connection, DisconnectReason reason)
    {
        m_outbound.Disconnect(connection, reason, string.Empty);
    }

    public void CoolDownAddress(ConnectionId connection)
    {
        m_outbound.CoolDownAddress(connection);
    }
}
}
