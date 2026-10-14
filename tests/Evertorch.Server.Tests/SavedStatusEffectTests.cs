using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Every status is saved by the checkpoint with its time left, which waits while the character is offline, and a
///     load restores it before the statistics are derived; a converter loaded unarmed and a status the content lacks are
///     dropped (Gameplay Systems §9.1; Persistence §6 to §8).
/// </summary>
[TestFixture]
public sealed class SavedStatusEffectTests
{
    private const int TickMs = 1000 / TestServer.TickRate;

    private static readonly StatusDefinitionId Focus = new("status.focus");
    private static readonly StatusDefinitionId IronGuard = new("status.iron_guard");
    private static readonly StatusDefinitionId FireWeapon = new("status.fire_weapon");
    private static readonly StatPercentages FocusThree = new(0, 100, 0, 0, 100, 0);
    private static readonly StatPercentages GuardThree = new(0, 0, 100, 0, 0, 0);

    private static ConnectionId EnterAgain(TestServer server, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, character);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State != SessionState.EnteringWorld);
        return connection;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId connection)
    {
        return server.SessionOf(connection).Character!;
    }

    private static StoredStatusEffect[] Checkpointed(TestServer server, ConnectionId connection)
    {
        server.Lifetime.QueueCheckpoint(CharacterOf(server, connection));
        server.Persistence.RunUntilIdle();
        return server.Store.Checkpoints.Last().StatusEffects!.ToArray();
    }

    [Test]
    public void Checkpoint_HoldsAnEffectToADay()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.StatusEffects.Apply(server.PlayerOf(connection), Focus, FocusThree, server.World.NowMs + 172_800_000);

        StoredStatusEffect saved = Checkpointed(server, connection).Single();

        Assert.That(saved.RemainingMs, Is.EqualTo(86_400_000));
    }

    [Test]
    public void Checkpoint_OnTheTickAnEffectEnds_StoresNoneOfIt()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        server.StatusEffects.Apply(player, Focus, FocusThree, server.World.NowMs + TickMs);
        StoredStatusEffect[]? saved = null;

        // The checkpoint is built after the commands, before the combat phase ends the effect that tick.
        server.AfterCommandsOnce(() => saved = Checkpointed(server, connection));
        server.Tick();

        Assert.That(saved, Is.Empty);
        Assert.That(player.StatusEffects, Is.Empty, "and the effect then ended");
    }

    [Test]
    public void Checkpoint_StoresEachActiveEffect_WithItsTimeLeftAndItsPercentages()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        server.StatusEffects.Apply(player, Focus, FocusThree, server.World.NowMs + 60_000);
        server.Tick(20);

        StoredStatusEffect saved = Checkpointed(server, connection).Single();

        Assert.That(
            (saved.StatusDefinitionId, saved.RemainingMs, saved.StatPercent),
            Is.EqualTo((Focus.Value, 59_000, FocusThree)));
    }

    [Test]
    public void Clock_IsSetAsATickStarts()
    {
        var server = new TestServer();

        server.Tick(41);

        Assert.That(server.World.NowMs, Is.EqualTo(2_000L));
    }

    [Test]
    public void Enter_WithAStoredConverter_AndNoWeaponWorn_DropsIt()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(7, statusEffects: new[] { new StoredStatusEffect(FireWeapon.Value, 600_000, default) });

        ConnectionId connection = EnterAgain(server, 7);

        Assert.That(server.PlayerOf(connection).StatusEffects, Is.Empty);
        Assert.That(server.PlayerOf(connection).AttackElement, Is.EqualTo(Element.Neutral));
    }

    [Test]
    public void Enter_WithAStoredEffect_RestoresItBeforeTheStatisticsAreDerived_SoTheStoredHpHolds()
    {
        // A level-24 Vanguard's VIT of 15 doubled by Iron Guard 3: (60 + 14 × 24) × 1.30 = 514, where 1.15 gives 455.
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(
            7,
            "job.vanguard",
            health: 514,
            level: 24,
            stats: new PrimaryStats(5, 5, 15, 5, 5, 5),
            statusEffects: new[] { new StoredStatusEffect(IronGuard.Value, 30_000, GuardThree) });

        ConnectionId connection = EnterAgain(server, 7);
        PlayerEntity player = server.PlayerOf(connection);

        Assert.That((player.MaxHealth, player.CurrentHealth), Is.EqualTo((514, 514)));
        Assert.That(player.StatusEffects.Select(effect => effect.Status), Is.EqualTo(new[] { IronGuard }));
    }

    [Test]
    public void Enter_WithAStoredStatusTheContentLacks_DropsAndLogsIt_AndTheNextCheckpointDeletesIt()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(
            7,
            statusEffects: new[]
            {
                new StoredStatusEffect(Focus.Value, 30_000, FocusThree),
                new StoredStatusEffect("status.forgotten", 30_000, default)
            });

        ConnectionId connection = EnterAgain(server, 7);
        StoredStatusEffect[] saved = Checkpointed(server, connection);

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.InWorld), "the character entered");
        Assert.That(server.PlayerOf(connection).StatusEffects.Select(effect => effect.Status),
            Is.EqualTo(new[] { Focus }));
        Assert.That(
            server.LifetimeLog.Entries.Where(entry => entry.EventId.Id == 1043).Select(entry => entry.Message),
            Has.Exactly(1).Contains("status.forgotten"));
        Assert.That(saved.Select(effect => effect.StatusDefinitionId), Is.EqualTo(new[] { Focus.Value }));
    }

    [Test]
    public void LogoutAndEnteringAgain_KeepAnEffectWithItsTimeLeft_WhichWaitedOffline()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(7);
        server.StatusEffects.Apply(server.PlayerOf(first), Focus, FocusThree, server.World.NowMs + 60_000);
        server.Tick(40);
        server.Disconnect(first);
        server.Tick(2);
        int left = server.Store.Stored(7).StatusEffects.Single().RemainingMs;
        server.Tick(400);

        ConnectionId again = EnterAgain(server, 7);
        PlayerEntity player = server.PlayerOf(again);

        Assert.That(left, Is.EqualTo(57_950), "41 ticks spent in the world, to the disconnect's tick");
        Assert.That(player.StatusEffects.Single().EndMs - server.World.NowMs, Is.EqualTo(left), "none spent offline");
        Assert.That(player.StatusEffects.Single().StatPercent, Is.EqualTo(FocusThree));
        Assert.That(
            server.Transport.ControlSentTo(again).Count(message => message.Opcode == MessageOpcode.StatusEffects),
            Is.EqualTo(1),
            "the baseline told the owner");
    }
}
}
