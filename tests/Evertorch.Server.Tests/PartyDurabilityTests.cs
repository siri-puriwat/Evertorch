using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A party change happens once or not at all on PostgreSQL 18 (Milestone 12 verification; Gameplay Systems §14;
///     Persistence §5): a replayed or stale command changes nothing more, a paused database refuses a change or leaves
///     its lost answer to the stored membership, two accepts racing for the last place let one in, two characters
///     accepting each other's invites found one party, and a restart loads the party as stored, an offline leader
///     still leading.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PartyDurabilityTests
{
    [TearDown]
    public void EndOutage()
    {
        m_sequences.Clear();
        if (m_isPaused)
        {
            m_database.Resume();
            m_isPaused = false;
        }
    }

    private const int CommandTimeoutMs = 1000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private readonly Dictionary<ConnectionId, uint> m_sequences = new();
    private PostgresFixture m_database = null!;
    private bool m_isPaused;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private TestServer NewServer()
    {
        return new TestServer(
            persistence: new PersistenceOptions { CommandTimeoutMs = CommandTimeoutMs, RetryBaseDelayMs = 1 },
            store: new PostgresGameStore(m_database.ConnectionString, TimeSpan.FromMilliseconds(CommandTimeoutMs)));
    }

    // The party's stored members in the order they joined, and its stored leader; none without a party.
    private (string[] Members, string Leader) StoredParty(string name)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT c.name, p.leader_character_id = c.id FROM party_members m "
            + "JOIN parties p ON p.id = m.party_id JOIN characters c ON c.id = m.character_id "
            + "WHERE m.party_id = (SELECT pm.party_id FROM party_members pm JOIN characters pc ON pc.id = pm.character_id "
            + $"WHERE pc.name = '{name}') ORDER BY m.join_order",
            connection);
        using NpgsqlDataReader reader = command.ExecuteReader();
        var members = new List<string>();
        string leader = string.Empty;
        while (reader.Read())
        {
            members.Add(reader.GetString(0));
            if (reader.GetBoolean(1))
            {
                leader = reader.GetString(0);
            }
        }

        return (members.ToArray(), leader);
    }

    private long Scalar(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    // The party as the server holds it: the members' names, and the leader's.
    private static (string[] Members, string Leader) HeldParty(TestServer server, ConnectionId player)
    {
        if (!server.Parties.TryGetParty(server.SessionOf(player).Character!.Character, out ServerParty? party))
        {
            return (Array.Empty<string>(), string.Empty);
        }

        return (party!.Members.Select(member => member.Name).ToArray(),
            party.Members.Single(member => member.CharacterId == party.LeaderCharacterId).Name);
    }

    private static void TickUntil(TestServer server, Func<bool> condition, string what)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < Limit)
        {
            server.Tick();
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.Fail($"Not reached within {Limit.TotalSeconds} s: {what}");
    }

    private ConnectionId Enter(TestServer server, string name)
    {
        ConnectionId connection = server.EnterWorldAs($"party-{name.ToLowerInvariant()}", name);
        m_sequences[connection] = 0;
        return connection;
    }

    private uint Next(ConnectionId connection)
    {
        m_sequences.TryGetValue(connection, out uint sequence);
        m_sequences[connection] = ++sequence;
        return sequence;
    }

    private static CommandRejectionReason[] Refusals(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected.Reason;
            })
            .ToArray();
    }

    // Invites and accepts, ticking until the stored party holds the invitee.
    private void Join(TestServer server, ConnectionId leader, string leaderName, ConnectionId invitee, string name)
    {
        server.SendPartyInvite(leader, name, Next(leader));
        server.Tick();
        server.SendPartyReply(invitee, leaderName, true, Next(invitee));
        TickUntil(server, () => StoredParty(name).Members.Contains(name), $"{name} joined");
        server.Tick();
    }

    [Test]
    public void AReplayedOrStaleAccept_JoinsOnce_AndARestartLoadsTheParty()
    {
        TestServer server = NewServer();
        ConnectionId anna = Enter(server, "DurAnna");
        ConnectionId bobby = Enter(server, "DurBobby");
        server.SendPartyInvite(anna, "DurBobby", Next(anna));
        server.Tick();

        uint accept = Next(bobby);
        server.SendPartyReply(bobby, "DurAnna", true, accept);
        server.SendPartyReply(bobby, "DurAnna", true, accept);
        server.SendPartyReply(bobby, "DurAnna", true, accept - 1);
        TickUntil(server, () => StoredParty("DurBobby").Members.Length == 2, "the join");
        server.Tick(2);

        Assert.That(StoredParty("DurAnna"), Is.EqualTo((new[] { "DurAnna", "DurBobby" }, "DurAnna")));
        Assert.That(server.SessionOf(bobby).RefusedCommands, Is.EqualTo(2), "the replay and the stale one, unanswered");
        Assert.That(Refusals(server, bobby), Is.Empty);
        Assert.That(
            Scalar("SELECT count(*) FROM parties p JOIN party_members m ON m.party_id = p.id "
                + "JOIN characters c ON c.id = m.character_id WHERE c.name = 'DurBobby'"),
            Is.EqualTo(1));

        server.Disconnect(anna);
        server.Disconnect(bobby);
        server.Tick(2);
        TestServer restarted = NewServer();
        ConnectionId back = Enter(restarted, "DurBobby");
        restarted.Tick();
        Assert.That(HeldParty(restarted, back), Is.EqualTo((new[] { "DurAnna", "DurBobby" }, "DurAnna")),
            "an offline leader still leads");
    }

    // Asked for while the database is known to be down, an accept is refused with 6 and the invite waits. Asked for
    // again just before the database pauses, its answer is lost; once the database answers, the stored membership
    // settles it, the server's copy always the stored one, and a later accept lands once (Persistence §5).
    [Test]
    public void AnAccept_AroundAPausedDatabase_IsRefusedOrSettled_NeverHalfDone()
    {
        TestServer server = NewServer();
        ConnectionId anna = Enter(server, "PauAnna");
        ConnectionId bobby = Enter(server, "PauBobby");
        server.SendPartyInvite(anna, "PauBobby", Next(anna));
        server.Tick();
        m_database.Pause();
        m_isPaused = true;
        server.Persistence.Probe();

        server.SendPartyReply(bobby, "PauAnna", true, Next(bobby));
        server.Tick();
        CommandRejectionReason[] inTheOutage = Refusals(server, bobby);
        int invitesInTheOutage = server.Parties.PendingInvites;
        m_database.Resume();
        m_isPaused = false;
        TickUntil(
            server,
            () =>
            {
                server.Persistence.Probe();
                return server.Persistence.State == DatabaseState.Available;
            },
            "the database available again");

        server.RunsPersistence = false;
        server.SendPartyReply(bobby, "PauAnna", true, Next(bobby));
        server.Tick();
        m_database.Pause();
        m_isPaused = true;
        server.RunsPersistence = true;
        server.Tick(5);
        m_database.Resume();
        m_isPaused = false;
        TickUntil(
            server,
            () => HeldParty(server, anna).Members.Length == 2 || Refusals(server, bobby).Length == 2,
            "the accept settled");
        (string[] Members, string Leader) settled = HeldParty(server, anna);
        (string[] Members, string Leader) stored = StoredParty("PauAnna");
        if (settled.Members.Length == 0)
        {
            Join(server, anna, "PauAnna", bobby, "PauBobby");
        }

        Assert.That(inTheOutage, Is.EqualTo(new[] { CommandRejectionReason.ServiceUnavailable }));
        Assert.That(invitesInTheOutage, Is.EqualTo(1), "the invite waits through the outage");
        Assert.That(settled.Members, Is.EqualTo(stored.Members), "the settled party is the stored one");
        Assert.That(StoredParty("PauBobby"), Is.EqualTo((new[] { "PauAnna", "PauBobby" }, "PauAnna")));
        Assert.That(HeldParty(server, bobby), Is.EqualTo(StoredParty("PauBobby")));
    }

    // A leader's departure, a removal, and a disband are stored as they happen, and a restart loads what is left.
    [Test]
    public void Departures_AreStoredAsTheyHappen_AndARestartLoadsWhatIsLeft()
    {
        TestServer server = NewServer();
        ConnectionId anna = Enter(server, "DepAnna");
        ConnectionId bobby = Enter(server, "DepBobby");
        ConnectionId cora = Enter(server, "DepCora");
        Join(server, anna, "DepAnna", bobby, "DepBobby");
        Join(server, anna, "DepAnna", cora, "DepCora");

        server.SendPartyLeave(anna, Next(anna));
        TickUntil(server, () => StoredParty("DepBobby").Members.Length == 2, "the leader left");
        (string[] Members, string Leader) afterLeave = StoredParty("DepBobby");
        server.SendPartyKick(bobby, "DepCora", Next(bobby));
        TickUntil(server, () => StoredParty("DepBobby").Members.Length == 0, "the removal disbanded it");
        server.Tick();

        Assert.That(afterLeave, Is.EqualTo((new[] { "DepBobby", "DepCora" }, "DepBobby")));
        Assert.That(StoredParty("DepCora").Members, Is.Empty);
        Assert.That(server.Parties.PartyCount, Is.Zero);
        TestServer restarted = NewServer();
        ConnectionId back = Enter(restarted, "DepBobby");
        restarted.Tick();
        Assert.That(HeldParty(restarted, back).Members, Is.Empty, "no party after the restart either");
    }

    // Two characters invite each other and both accept in one tick: one party is founded, led by the first inviter
    // whose invite was accepted, and the other accept is refused, never a key violation (Persistence §5).
    [Test]
    public void MutualInvites_BothAccepted_FoundOneParty()
    {
        TestServer server = NewServer();
        ConnectionId anna = Enter(server, "MutAnna");
        ConnectionId bobby = Enter(server, "MutBobby");
        server.SendPartyInvite(anna, "MutBobby", Next(anna));
        server.SendPartyInvite(bobby, "MutAnna", Next(bobby));
        server.Tick();

        server.SendPartyReply(bobby, "MutAnna", true, Next(bobby));
        server.SendPartyReply(anna, "MutBobby", true, Next(anna));
        TickUntil(server, () => StoredParty("MutAnna").Members.Length == 2, "a party founded");
        server.Tick(3);

        Assert.That(StoredParty("MutBobby"), Is.EqualTo((new[] { "MutAnna", "MutBobby" }, "MutAnna")));
        Assert.That(Refusals(server, anna), Is.EqualTo(new[] { CommandRejectionReason.Busy }));
        Assert.That(
            Scalar("SELECT count(*) FROM parties p WHERE p.leader_character_id IN "
                + "(SELECT id FROM characters WHERE name IN ('MutAnna', 'MutBobby'))"),
            Is.EqualTo(1));
    }

    // Two invitees accept in one tick for a party of four: one joins, the other is refused while the change is in
    // flight and then, the party full, with 13; the stored party never passes five.
    [Test]
    public void TwoAcceptsRacingForTheLastPlace_LetOneIn()
    {
        TestServer server = NewServer();
        ConnectionId leader = Enter(server, "RaceLead");
        foreach (string name in new[] { "RaceTwo", "RaceThree", "RaceFour" })
        {
            Join(server, leader, "RaceLead", Enter(server, name), name);
        }

        ConnectionId first = Enter(server, "RaceFirst");
        ConnectionId second = Enter(server, "RaceSecond");
        server.SendPartyInvite(leader, "RaceFirst", Next(leader));
        server.SendPartyInvite(leader, "RaceSecond", Next(leader));
        server.Tick();

        server.SendPartyReply(first, "RaceLead", true, Next(first));
        server.SendPartyReply(second, "RaceLead", true, Next(second));
        TickUntil(server, () => StoredParty("RaceLead").Members.Length == 5, "the last place taken");
        server.SendPartyReply(second, "RaceLead", true, Next(second));
        server.Tick(3);

        Assert.That(StoredParty("RaceLead").Members.Last(), Is.EqualTo("RaceFirst"));
        Assert.That(
            Refusals(server, second),
            Is.EqualTo(new[] { CommandRejectionReason.Busy, CommandRejectionReason.RequirementNotMet }));
        Assert.That(Scalar("SELECT max(n) FROM (SELECT count(*) AS n FROM party_members GROUP BY party_id) counted"),
            Is.LessThanOrEqualTo(PartyRegistry.MaxMembers));
    }
}
}
