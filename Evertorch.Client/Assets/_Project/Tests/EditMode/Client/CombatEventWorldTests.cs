using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class CombatEventWorldTests
{
    private static readonly EntityId Slime = new EntityId(300);

    private static ClientWorld CreateWorld()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(2, 8));
        world.OnSpawn(
            new EntitySpawn(
                Slime,
                EntityKind.Monster,
                "monster.a",
                ClientTestGrids.Center(4, 8),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000));
        return world;
    }

    [Test]
    public void CharacterHealth_UpdatesTheLocalHealth()
    {
        ClientWorld world = CreateWorld();
        uint enteredWith = world.LocalHealth;

        world.OnCharacterHealth(new CharacterHealth(40, 71));

        Assert.That(enteredWith, Is.EqualTo(71u));
        Assert.That(world.LocalHealth, Is.EqualTo(40u));
        Assert.That(world.LocalMaximumHealth, Is.EqualTo(71u));
        Assert.That(world.AttackRange, Is.EqualTo(1.5f));
    }

    [Test]
    public void Damage_ToAKnownMonster_UpdatesItsRatioAndIsAnnounced()
    {
        ClientWorld world = CreateWorld();
        var received = new List<Damage>();
        world.DamageReceived += received.Add;

        world.OnDamage(new Damage(ClientWorldFixture.LocalEntity, Slime, CombatResult.Hit, 10, 5, 800));

        Assert.That(world.Remotes[Slime].HealthPermille, Is.EqualTo(800));
        Assert.That(received.Count, Is.EqualTo(1));
    }

    [Test]
    public void EntityDied_MarksTheMonsterDead_SoItIsNoLongerACandidate()
    {
        ClientWorld world = CreateWorld();
        var candidates = new List<PickCandidate>();

        world.OnEntityDied(new EntityDied(Slime, ClientWorldFixture.LocalEntity, 9));
        world.CollectTargetCandidates(candidates);

        Assert.That(world.Remotes[Slime].IsDead, Is.True);
        Assert.That(world.Remotes[Slime].HealthPermille, Is.Zero);
        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public void EntityDiedAndRevived_ForTheLocalEntity_FollowItsLifeState()
    {
        ClientWorld world = CreateWorld();

        world.OnEntityDied(new EntityDied(ClientWorldFixture.LocalEntity, Slime, 9));
        bool wasDead = world.IsLocalDead;
        var revived = new EntityRevived(
            ClientWorldFixture.LocalEntity,
            ClientTestGrids.Center(2, 8),
            new WorldDirection(0f, 1f),
            12);
        world.OnEntityRevived(revived);

        Assert.That(wasDead, Is.True);
        Assert.That(world.IsLocalDead, Is.False);
    }

    [Test]
    public void Events_AboutEntitiesWithoutASpawn_AreCountedAndIgnored()
    {
        ClientWorld world = CreateWorld();
        var unknown = new EntityId(999);
        int announced = 0;
        world.AttackStartedReceived += _ => announced++;
        world.DamageReceived += _ => announced++;
        world.EntityDiedReceived += _ => announced++;
        world.EntityRevivedReceived += _ => announced++;
        var timing = new AttackTiming(
            TimeSpan.FromMilliseconds(1200),
            TimeSpan.FromMilliseconds(600),
            TimeSpan.FromMilliseconds(600),
            TimeSpan.FromMilliseconds(300));

        world.OnAttackStarted(new AttackStarted(unknown, Slime, 1, timing));
        world.OnDamage(new Damage(Slime, unknown, CombatResult.Hit, 5, 1, 0));
        world.OnEntityDied(new EntityDied(unknown, default, 1));
        world.OnEntityRevived(new EntityRevived(unknown, default, new WorldDirection(0f, 1f), 1));

        Assert.That(world.UnknownEntityEvents, Is.EqualTo(4));
        Assert.That(announced, Is.Zero);
    }
}
}
