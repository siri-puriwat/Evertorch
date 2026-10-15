using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Ailments as statuses on players and monsters (Gameplay Systems §9.1): the state flags everyone who knows the body
///     sees, poison's passive drain every second that never kills and stops regeneration, the end of what is due before
///     the drain, a monster's ailments alone and their end at its death, and the owner's list with its ailments first.
/// </summary>
[TestFixture]
public sealed class AilmentStatusTests
{
    private static readonly StatusDefinitionId Poisoned = new("status.poisoned");
    private static readonly StatusDefinitionId Frozen = new("status.frozen");
    private static readonly StatusDefinitionId Silenced = new("status.silenced");
    private static readonly StatusDefinitionId Focus = new("status.focus");
    private static readonly StatPercentages FocusOne = new(0, 40, 0, 0, 40, 0);

    private static Damage[] DamageSentTo(TestServer server, ConnectionId connection)
    {
        return server.Transport.SentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.Damage)
            .Select(message => Damage.TryRead(message.Payload, out Damage read) ? read : default)
            .ToArray();
    }

    private static int HealthToldTo(TestServer server, ConnectionId connection)
    {
        return server.Transport.SentTo(connection).Count(message => message.Opcode == MessageOpcode.CharacterHealth);
    }

    private static (TestServer Server, ConnectionId Owner, ConnectionId Watcher, PlayerEntity Player) TwoPlayers()
    {
        var server = new TestServer();
        ConnectionId owner = server.EnterWorld(7);
        ConnectionId watcher = server.EnterWorld(8);
        server.Tick(2);
        PlayerEntity player = server.PlayerOf(owner);
        player.CurrentHealth = player.MaxHealth;
        return (server, owner, watcher, player);
    }

    private static MonsterEntity SlimeOf(TestServer server)
    {
        return server.World.Maps.SelectMany(map => map.Monsters).First();
    }

    [Test]
    public void Ailments_StandInTheStateFlags_WhichEveryWatcherSees_AndLeaveWithTheirEnd()
    {
        (TestServer server, _, ConnectionId watcher, PlayerEntity player) = TwoPlayers();

        server.StatusEffects.Apply(player, Frozen, default, server.World.NowMs + 2000);
        server.StatusEffects.Apply(player, Silenced, default, server.World.NowMs + 5000);
        server.StatusEffects.Apply(player, Focus, FocusOne, server.World.NowMs + 5000);
        EntityStateFlags applied = player.StateFlags;
        server.Transport.ClearSent();
        server.Tick();
        EntityStateFlags seen = server.Transport.SnapshotsSentTo(watcher).Last().Entities
            .Single(entity => entity.Entity == player.Id).StateFlags;
        server.Tick(3 * TestServer.TickRate);

        Assert.That(applied, Is.EqualTo(EntityStateFlags.Frozen | EntityStateFlags.Silenced));
        Assert.That(seen & (EntityStateFlags.Frozen | EntityStateFlags.Silenced),
            Is.EqualTo(EntityStateFlags.Frozen | EntityStateFlags.Silenced));
        Assert.That(player.StateFlags, Is.EqualTo(EntityStateFlags.Silenced), "the freeze is over");
        Assert.That(player.IsHeld, Is.False);
    }

    [Test]
    public void EntityFlags_OfEachAilment_AreItsBit()
    {
        Assert.That(
            new[]
            {
                Ailment.None, Ailment.Poison, Ailment.Stun, Ailment.Freeze, Ailment.Sleep, Ailment.Silence,
                Ailment.Blind
            }.Select(WorldEntity.FlagOf),
            Is.EqualTo(
                new List<EntityStateFlags>
                {
                    EntityStateFlags.None, EntityStateFlags.Poisoned, EntityStateFlags.Stunned,
                    EntityStateFlags.Frozen, EntityStateFlags.Asleep, EntityStateFlags.Silenced,
                    EntityStateFlags.Blinded
                }));
    }

    [Test]
    public void Monster_HoldsAilmentsAlone_AndItsDeathEndsThem()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        server.EnterWorld(7);
        server.Tick(2);
        MonsterEntity slime = SlimeOf(server);
        MapInstance map = server.World.Maps.Single(candidate => candidate.Monsters.Contains(slime));
        Action buff = () => server.StatusEffects.Apply(slime, Focus, FocusOne, server.World.NowMs + 1000);

        server.StatusEffects.Apply(slime, Frozen, default, server.World.NowMs + 8000);
        server.StatusEffects.Apply(slime, Poisoned, default, server.World.NowMs + 8000);
        bool wasHeld = slime.IsHeld;
        server.Combat.Kill(map, slime, null, server.CurrentTick);

        Assert.That(buff, Throws.InvalidOperationException);
        Assert.That(wasHeld, Is.True);
        Assert.That(slime.StatusEffects, Is.Empty);
        Assert.That(slime.StateFlags, Is.EqualTo(EntityStateFlags.Dead), "dead, with no ailment");
    }

    [Test]
    public void Poison_DueWithItsEnd_DoesNotDrain()
    {
        (TestServer server, ConnectionId owner, _, PlayerEntity player) = TwoPlayers();
        server.Transport.ClearSent();

        server.StatusEffects.Apply(player, Poisoned, default, server.World.NowMs + 3000);
        server.Tick(10 * TestServer.TickRate);

        Assert.That(DamageSentTo(server, owner), Has.Length.EqualTo(2), "at 1 s and 2 s; the end comes first at 3 s");
    }

    [Test]
    public void Poison_OnAMonster_DrainsAThirdAsHard_Passively()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId watcher = server.EnterWorld(7);
        server.Tick(2);
        MonsterEntity slime = SlimeOf(server);
        int max = slime.MaxHealth;
        int loss = 2 + max * 5 / 1000;
        server.Transport.ClearSent();

        server.StatusEffects.Apply(slime, Poisoned, default, server.World.NowMs + 20_000);
        server.Tick(TestServer.TickRate);

        Assert.That(slime.CurrentHealth, Is.EqualTo(max - loss));
        Assert.That(slime.HasAilment(Ailment.Poison), Is.True);
        Assert.That((slime.DamageLog.Count, slime.Brain.LastAttacker), Is.EqualTo((0, default(EntityId))),
            "no damage log and no grudge");
        Assert.That(server.SessionOf(watcher).Knows(slime.Id), Is.True);
        Damage told = DamageSentTo(server, watcher).Single();
        Assert.That((told.Source, told.Result, told.Amount, told.TargetHealthPermille),
            Is.EqualTo((default(EntityId), CombatResult.Poison, (uint)loss, slime.SharedHealthPermille)));
    }

    [Test]
    public void Poison_OnAPlayer_DrainsEverySecondToAQuarter_SeenByEveryoneWhoKnowsIt_AndNeverKills()
    {
        (TestServer server, ConnectionId owner, ConnectionId watcher, PlayerEntity player) = TwoPlayers();
        int max = player.MaxHealth;
        int loss = 2 + max * 15 / 1000;
        server.Transport.ClearSent();

        server.StatusEffects.Apply(player, Poisoned, default, server.World.NowMs + 600_000);
        server.Tick(TestServer.TickRate);
        int afterOne = player.CurrentHealth;
        Damage[] watched = DamageSentTo(server, watcher);
        Damage[] owned = DamageSentTo(server, owner);
        int toldHealth = HealthToldTo(server, owner);
        server.Tick(300 * TestServer.TickRate);

        Assert.That(afterOne, Is.EqualTo(max - loss), "one tick, a second after the start");
        Assert.That(
            watched.Select(damage => (damage.Source, damage.Target, damage.Result, damage.Amount)),
            Is.EqualTo(new[] { (default(EntityId), player.Id, CombatResult.Poison, (uint)loss) }),
            "a watcher sees the tick, with no source");
        Assert.That(owned.Single().Result, Is.EqualTo(CombatResult.Poison));
        Assert.That(toldHealth, Is.EqualTo(1), "the owner hears its HP");
        Assert.That(player.CurrentHealth,
            Is.GreaterThan(Math.Max(max / 4 - loss, 0)).And.AtMost(Math.Max(max / 4, loss)),
            "it stops at a quarter, regeneration stopped");
        Assert.That(player.IsDead, Is.False);
        Assert.That(player.HasAilment(Ailment.Poison), Is.True);
    }

    [Test]
    public void Poison_SavedAndLoaded_DrainsAgainASecondAfterTheLoad()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(7, statusEffects: new[] { new StoredStatusEffect(Poisoned.Value, 30_000, default) });
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, 7);
        server.SendEnterWorld(connection, 7);
        server.TickUntil(() => server.SessionOf(connection).State != SessionState.EnteringWorld);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentHealth = player.MaxHealth;
        ActiveStatusEffect loaded = player.StatusEffects.Single();

        server.Tick(TestServer.TickRate + 1);

        Assert.That((loaded.Ailment, player.HasAilment(Ailment.Poison)), Is.EqualTo((Ailment.Poison, true)));
        Assert.That(player.CurrentHealth, Is.EqualTo(player.MaxHealth - (2 + player.MaxHealth * 15 / 1000)));
    }

    [Test]
    public void Poison_StopsRegeneration_UntilItEnds()
    {
        (TestServer server, _, _, PlayerEntity player) = TwoPlayers();
        player.CurrentHealth = 1;
        player.CurrentSpirit = 0;

        server.StatusEffects.Apply(player, Poisoned, default, server.World.NowMs + 30_000);
        server.Tick(20 * TestServer.TickRate);
        (int Health, int Spirit) poisoned = (player.CurrentHealth, player.CurrentSpirit);
        server.Tick(20 * TestServer.TickRate);

        Assert.That(poisoned, Is.EqualTo((1, 0)), "no gain while poisoned, and no tick below a quarter");
        Assert.That(player.CurrentHealth, Is.GreaterThan(1), "the gains are back once it ends");
        Assert.That(player.StatusEffects, Is.Empty);
    }

    [Test]
    public void StatusEffects_ToTheOwner_ListAilmentsFirst()
    {
        (TestServer server, ConnectionId owner, _, PlayerEntity player) = TwoPlayers();
        server.StatusEffects.Apply(player, Focus, FocusOne, server.World.NowMs + 60_000);
        server.StatusEffects.Apply(player, Silenced, default, server.World.NowMs + 15_000);
        server.Transport.ClearSent();

        server.Tick();

        StatusEffects told = server.Transport.ControlSentTo(owner)
            .Where(message => message.Opcode == MessageOpcode.StatusEffects)
            .Select(message => StatusEffects.TryRead(message.Payload, out StatusEffects? read) ? read! : null)
            .Last()!;
        Assert.That(told.Effects.Select(entry => entry.Status), Is.EqualTo(new[] { Silenced, Focus }));
        Assert.That(player.StatusEffects.Select(effect => effect.Status), Is.EqualTo(new[] { Focus, Silenced }),
            "the body keeps them in the order they started");
    }
}
}
