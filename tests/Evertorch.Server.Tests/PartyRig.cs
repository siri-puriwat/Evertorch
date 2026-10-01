using System.Collections.Generic;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A test server with each connection's command sequence, and the steps of the party's commands, each ticked
///     until answered.
/// </summary>
internal sealed class PartyRig
{
    private readonly Dictionary<ConnectionId, uint> m_sequences = new();

    public PartyRig(TestServer? server = null)
    {
        Server = server ?? new TestServer();
    }

    public TestServer Server { get; }

    public ConnectionId Enter(long character)
    {
        ConnectionId connection = Server.EnterWorld(character);
        m_sequences[connection] = 0;
        return connection;
    }

    public uint Next(ConnectionId connection)
    {
        m_sequences.TryGetValue(connection, out uint sequence);
        m_sequences[connection] = ++sequence;
        return sequence;
    }

    public uint Invite(ConnectionId connection, string name)
    {
        uint sequence = Next(connection);
        Server.SendPartyInvite(connection, name, sequence);
        Server.Tick();
        return sequence;
    }

    public uint Reply(ConnectionId connection, string inviter, bool isAccepted)
    {
        uint sequence = Next(connection);
        Server.SendPartyReply(connection, inviter, isAccepted, sequence);
        Server.Tick(2);
        return sequence;
    }

    public uint Leave(ConnectionId connection)
    {
        uint sequence = Next(connection);
        Server.SendPartyLeave(connection, sequence);
        Server.Tick(2);
        return sequence;
    }

    public uint Kick(ConnectionId connection, string name)
    {
        uint sequence = Next(connection);
        Server.SendPartyKick(connection, name, sequence);
        Server.Tick(2);
        return sequence;
    }

    public uint Lead(ConnectionId connection, string name)
    {
        uint sequence = Next(connection);
        Server.SendPartyLead(connection, name, sequence);
        Server.Tick(2);
        return sequence;
    }

    public void Join(ConnectionId leader, string leaderName, ConnectionId invitee, string inviteeName)
    {
        Invite(leader, inviteeName);
        Reply(invitee, leaderName, true);
    }
}
}
