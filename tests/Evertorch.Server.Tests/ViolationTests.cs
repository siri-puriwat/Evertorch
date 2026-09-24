using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The violation score and what crossing it does (Network Protocol §3, §11): the connection closes with
///     <c>RateLimited</c> or <c>Kicked</c>, the character leaves at once, and the account or the address waits out a
///     cooldown.
/// </summary>
[TestFixture]
public sealed class ViolationTests
{
    private static readonly AbuseOptions Defaults = new();
    private static readonly int ViolationsToClose = Defaults.ViolationThreshold / ViolationScore.Points;

    private static IReadOnlyDictionary<string, object?>[] Audited(TestServer server, string eventName, LogLevel level)
    {
        return server.AuditLogger.Entries
            .Where(entry => entry.EventId.Name == eventName)
            .Select(entry =>
            {
                Assert.That(entry.Level, Is.EqualTo(level), eventName);
                return entry.Fields;
            })
            .ToArray();
    }

    private static void SendGarbage(TestServer server, ConnectionId connection, int count)
    {
        for (int index = 0; index < count; index++)
        {
            server.Inbound.OnPayload(connection, ProtocolChannel.Control, new byte[] { 0xFF, 0x7F, 0x01 });
        }
    }

    private static TestServer ServerWithCooldown(int cooldownMs)
    {
        return new TestServer(abuseOptions: new AbuseOptions { KickCooldownMs = cooldownMs });
    }

    [Test]
    public void AccountCooldown_RefusesItsSignInWithRateLimitedUntilItEnds()
    {
        TestServer server = ServerWithCooldown(1000);
        ConnectionId kicked = server.EnterWorld(7);
        SendGarbage(server, kicked, ViolationsToClose);
        server.Tick();

        ConnectionId early = server.Connect();
        server.SignIn(early, $"{TestServer.DevelopmentToken}7");
        ConnectionId other = server.Connect();
        server.SignIn(other, $"{TestServer.DevelopmentToken}8");

        Assert.That(server.Transport.Disconnects[kicked], Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(server.Transport.Disconnects[early], Is.EqualTo(DisconnectReason.RateLimited));
        Assert.That(server.SessionOf(other).Account, Is.Not.Null, "another account is not affected");
        Assert.That(server.Transport.CooledDownAddresses, Is.Empty, "a kick after sign-in spares the address");

        server.Tick(TestServer.TickRate);
        ConnectionId later = server.Connect();
        server.SignIn(later, $"{TestServer.DevelopmentToken}7");
        Assert.That(server.SessionOf(later).Account, Is.Not.Null, "the cooldown is over");
    }

    [Test]
    public void AddressCooldown_IsAskedForWhenTheConnectionNeverSignedIn()
    {
        var server = new TestServer();
        ConnectionId stranger = server.Connect();
        server.Tick();

        SendGarbage(server, stranger, ViolationsToClose);
        server.Tick();

        Assert.That(server.Transport.Disconnects[stranger], Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(server.Transport.CooledDownAddresses, Is.EquivalentTo(new[] { stranger }));
    }

    [Test]
    public void Audit_OfOneConnection_IsLimitedPerSecond()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        for (uint sequence = 1; sequence <= 30; sequence++)
        {
            server.SendAttack(player, new EntityId(999999), sequence);
        }

        server.Tick();

        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(30));
        Assert.That(
            server.AuditLogger.Entries.Count(entry => entry.EventId.Name == "CommandRefused"),
            Is.EqualTo(AuditLog.EventsPerConnection));
    }

    [Test]
    public void Audit_RecordsRefusalsAtDebugAndViolationsAndTheDisconnectAtInformation()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        long character = server.SessionOf(player).Character!.Character.Value;
        server.SendAttack(player, new EntityId(999999), 1);
        server.Tick();

        // A second later, so the connection's share of the log is whole again.
        server.Tick(TestServer.TickRate);
        SendGarbage(server, player, ViolationsToClose);
        server.Tick();

        IReadOnlyDictionary<string, object?> refused = Audited(server, "CommandRefused", LogLevel.Debug).Single();
        Assert.That(refused["Command"], Is.EqualTo(InboundEventKind.Attack));
        Assert.That(refused["Reason"], Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(refused["Character"], Is.EqualTo(character));

        IReadOnlyDictionary<string, object?>[] scored = Audited(server, "ViolationScored", LogLevel.Information);
        Assert.That(scored, Has.Length.EqualTo(ViolationsToClose));
        Assert.That(scored.Last()["Violation"], Is.EqualTo(Violation.Malformed));

        IReadOnlyDictionary<string, object?> closed =
            Audited(server, "ViolationDisconnect", LogLevel.Information).Single();
        Assert.That(closed["Reason"], Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(closed["Connection"], Is.EqualTo(player.Value));
        Assert.That(closed["Account"], Is.Not.EqualTo(0L));
        Assert.That(closed["Character"], Is.EqualTo(character));
    }

    [Test]
    public void ControlOverTheBudget_ClosesAsAViolationDisconnect()
    {
        var server = new TestServer(reconnectGraceMs: 30000);
        ConnectionId player = server.EnterWorld(7);

        // Cancels belong to no bucket, so only the per-peer budget stops them.
        for (uint sequence = 1; sequence <= Defaults.PeerMessageBurst + 1; sequence++)
        {
            server.SendCancel(player, sequence);
        }

        server.Tick();

        Assert.That(server.Transport.Disconnects[player], Is.EqualTo(DisconnectReason.RateLimited));
        Assert.That(server.Sessions.Characters, Is.Empty, "removed at once, with no grace period");
        Assert.That(server.SessionManager.ViolationDisconnects, Is.EqualTo(1));
    }

    [Test]
    public void ExpelledCharacter_WithAPickupInFlight_CannotBeAttachedBeforeItLeaves()
    {
        var server = new TestServer(reconnectGraceMs: 30000);
        ConnectionId kicked = server.EnterWorld(1);
        ConnectionId sameAccount = server.Connect();
        server.SignInWithCharacter(sameAccount, 1);
        PlayerEntity player = server.PlayerOf(kicked);
        var beside = new WorldPosition(player.Position.X + 1f, player.Position.Y, player.Position.Z);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId("item.material.slime_gel"),
            2,
            beside,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.RunsPersistence = false;
        server.SendPickup(kicked, drop.Id, 1);
        server.Tick();
        SendGarbage(server, kicked, ViolationsToClose);
        server.Tick();

        server.SendEnterWorld(sameAccount, 1);
        server.Tick();

        Assert.That(server.Transport.Disconnects[kicked], Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(server.SessionOf(sameAccount).State, Is.EqualTo(SessionState.Authenticated), "not attached");
        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(1), "it waits for its pickup");

        server.RunsPersistence = true;
        server.Tick(3);

        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(2));
    }

    [Test]
    public void GameplayRefusalsStaleMovementAndOutOfStateCommands_AreNeverScored()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        ConnectionId selecting = server.Connect();
        server.SignInWithCharacter(selecting, 8);

        for (uint sequence = 1; sequence <= 30; sequence++)
        {
            server.SendAttack(player, new EntityId(999999), sequence);
            server.SendAttack(selecting, new EntityId(999999), sequence);
        }

        server.SendMove(player, 10, 1f, 0f);
        server.SendMove(player, 5, 1f, 0f);
        server.SendMove(player, 3, 0f, 1f);
        server.Tick();

        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(30));
        Assert.That(server.SessionOf(player).Violations!.Value, Is.Zero);
        Assert.That(server.SessionOf(selecting).Violations!.Value, Is.Zero);
        Assert.That(server.SessionManager.Violations, Is.Zero);
    }

    [Test]
    public void InputOverTheBudget_IsScoredAsRateExcess()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        for (uint sequence = 1; sequence <= Defaults.PeerMessageBurst + ViolationsToClose; sequence++)
        {
            server.SendMove(player, sequence, 1f, 0f);
        }

        server.Tick();

        Assert.That(server.Transport.Disconnects[player], Is.EqualTo(DisconnectReason.RateLimited));
        Assert.That(server.SessionManager.Violations, Is.EqualTo(ViolationsToClose));
    }

    [Test]
    public void LimitsOff_NothingIsScoredOrClosed()
    {
        var server = new TestServer(isAbuseControlEnabled: false);
        ConnectionId player = server.EnterWorld(7);

        SendGarbage(server, player, 100);
        server.Tick();

        Assert.That(server.Transport.Disconnects, Is.Empty);
        Assert.That(server.SessionOf(player).Violations, Is.Null);
        Assert.That(server.SessionManager.Violations, Is.Zero);
    }

    [Test]
    public void Malformed_BelowTheThreshold_KeepsTheConnection_AndAtItClosesWithKicked()
    {
        var server = new TestServer();
        ConnectionId below = server.EnterWorld(7);
        ConnectionId at = server.EnterWorld(8);

        SendGarbage(server, below, ViolationsToClose - 1);
        SendGarbage(server, at, ViolationsToClose);
        server.Tick();

        Assert.That(server.Transport.Disconnects.Keys, Is.EquivalentTo(new[] { at }));
        Assert.That(server.Transport.Disconnects[at], Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(
            server.SessionOf(below).Violations!.Value,
            Is.EqualTo((ViolationsToClose - 1) * ViolationScore.Points));
    }

    [Test]
    public void RateExcess_ThatCrossesTheThreshold_ClosesWithRateLimited()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        for (uint sequence = 1; sequence <= Defaults.CombatCommandBurst + ViolationsToClose; sequence++)
        {
            server.SendAttack(player, new EntityId(999999), sequence);
        }

        server.Tick();

        Assert.That(server.Transport.Disconnects[player], Is.EqualTo(DisconnectReason.RateLimited));
        Assert.That(server.SessionManager.ThrottledCommands, Is.EqualTo(ViolationsToClose));
        Assert.That(
            server.Transport.ControlOpcodesSentTo(player).Count(opcode => opcode == MessageOpcode.CommandRejected),
            Is.EqualTo(Defaults.CombatCommandBurst + ViolationsToClose),
            "the command that crossed the threshold was still answered");
    }

    [Test]
    public void RepeatedHelloAndStaleCommand_AreScored()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        server.SendAttack(player, new EntityId(999999), 5);
        server.Tick();

        server.SendHello(player);
        server.SendAttack(player, new EntityId(999999), 5);
        server.Tick();

        Assert.That(server.SessionOf(player).Violations!.Value, Is.EqualTo(2 * ViolationScore.Points));
        Assert.That(server.SessionManager.Violations, Is.EqualTo(2));
    }

    [Test]
    public void Score_DecaysWithTheTicksThatPass()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        SendGarbage(server, player, ViolationsToClose - 1);
        server.Tick();

        // Two seconds forgive one violation at the default decay.
        server.Tick(2 * TestServer.TickRate);
        SendGarbage(server, player, 1);
        server.Tick();

        Assert.That(server.Transport.Disconnects, Is.Empty);
        Assert.That(
            server.SessionOf(player).Violations!.Value,
            Is.EqualTo((ViolationsToClose - 1) * ViolationScore.Points).Within(1d));
    }

    [Test]
    public void Status_CountsViolationsAndTheirDisconnects()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        SendGarbage(server, player, ViolationsToClose);
        server.TickUntilPublished();

        AbuseStatus abuse = server.Status.Current.Abuse;
        Assert.That(abuse.Violations, Is.EqualTo(ViolationsToClose));
        Assert.That(abuse.ViolationDisconnects, Is.EqualTo(1));
    }

    [Test]
    public void ViolationDisconnect_RemovesTheCharacterAtOnceWithACheckpoint()
    {
        var server = new TestServer(reconnectGraceMs: 30000);
        ConnectionId player = server.EnterWorld(7);
        CharacterSession character = server.SessionOf(player).Character!;

        SendGarbage(server, player, ViolationsToClose);
        server.Tick();
        server.Tick();

        Assert.That(server.Sessions.Characters, Is.Empty);
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(character.IsExpelled, Is.True);
        Assert.That(
            server.Store.Checkpoints.Select(checkpoint => checkpoint.CharacterId),
            Does.Contain(character.Character.Value));
    }
}
}
