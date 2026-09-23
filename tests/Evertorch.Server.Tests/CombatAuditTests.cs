using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Gaps the Milestone 3 audit of range, interruption, invalid targets, death, leash, and respawn found in the
///     other fixtures.
/// </summary>
[TestFixture]
public sealed class CombatAuditTests
{
    [Test]
    public void Monster_AttacksOnlyWithinItsOwnRange_WithoutThePlayersTolerance()
    {
        var rig = new CombatRig(1.8f);
        rig.Slime.Target = rig.Entity.Id;
        rig.Slime.Combat.IsAutoAttacking = true;

        rig.Server.Tick(40);
        int startsBeyondItsRange = rig.Starts().Count;
        rig.StandBeside(rig.Slime, 1.4f);
        rig.TickUntilStarts(1, 40);

        Assert.That(startsBeyondItsRange, Is.Zero, "1.8 m is within a player's 1.5 + 0.5 but not the slime's 1.5");
        Assert.That(rig.Starts().Single().Attacker, Is.EqualTo(rig.Slime.Id));
    }

    [Test]
    public void Slime_KillingThePlayer_ThenARespawn_LetsThePlayerFightAgain()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Entity.CurrentHealth = 1;
        rig.Slime.Target = rig.Entity.Id;
        rig.Slime.Combat.IsAutoAttacking = true;
        for (int tick = 0; tick < 100 && !rig.Entity.IsDead; tick++)
        {
            rig.Server.Tick();
        }

        EntityDied death = rig.Deaths().Single();
        CharacterHealth health = rig.Received(
                MessageOpcode.CharacterHealth,
                payload => CharacterHealth.TryRead(payload, out CharacterHealth m) ? m : (CharacterHealth?)null)
            .Last();
        Assert.That(rig.Entity.IsDead, Is.True);
        Assert.That(death.Entity, Is.EqualTo(rig.Entity.Id));
        Assert.That(death.Source, Is.EqualTo(rig.Slime.Id));
        Assert.That(health.Current, Is.Zero);
        Assert.That(rig.Slime.Target, Is.EqualTo(default(EntityId)), "the slime drops its dead target");
        Assert.That(rig.Slime.Combat.IsAutoAttacking, Is.False);

        rig.Server.SendRespawn(rig.Player, 100);
        rig.Server.Tick();
        rig.StandBeside(rig.Slime, 1.2f);
        rig.Server.SendAttack(rig.Player, rig.Slime.Id, 101);
        rig.Server.Transport.ClearSent();
        rig.TickUntilStarts(1, 60);

        Assert.That(rig.Entity.IsDead, Is.False);
        Assert.That(rig.Entity.CurrentHealth, Is.EqualTo(rig.Entity.MaxHealth));
        Assert.That(rig.Server.SessionOf(rig.Player).RefusedCommands, Is.Zero);
        Assert.That(rig.Entity.Target, Is.EqualTo(rig.Slime.Id));
        Assert.That(rig.Starts().Single().Attacker, Is.EqualTo(rig.Entity.Id));
    }

    [Test]
    public void TargetAndAttack_OnACorpse_AreRefusedAndCounted()
    {
        var rig = new CombatRig();
        rig.Server.Combat.Kill(rig.Map, rig.Slime, null, rig.Server.CurrentTick);
        rig.Server.Tick();

        rig.Server.SendTarget(rig.Player, rig.Slime.Id);
        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(5);

        Assert.That(rig.Map.Contains(rig.Slime.Id), Is.True, "the corpse is still there");
        Assert.That(rig.Server.SessionOf(rig.Player).RefusedCommands, Is.EqualTo(2));
        Assert.That(rig.Entity.Target, Is.EqualTo(default(EntityId)));
        Assert.That(rig.Starts(), Is.Empty);
    }
}
}
