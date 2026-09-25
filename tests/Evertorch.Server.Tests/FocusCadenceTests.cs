using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Focus shortens the attack interval as the research note's vector says, 940 ms to 920 ms, from the first swing
///     that begins after it (Gameplay Systems §2, §9.1); a swing already under way keeps its interval.
/// </summary>
[TestFixture]
public sealed class FocusCadenceTests
{
    [Test]
    public void Focus_ShortensTheSwingsThatBeginAfterIt()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 10_000;
        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(40);
        rig.Server.TickUntil(() => !rig.Entity.Combat.IsSwinging);
        int swingsBefore = rig.Starts().Count;

        rig.Server.SendUseSkill(rig.Player, "skill.focus", default, 100);
        rig.Server.Tick(60);

        List<AttackStarted> starts = rig.Starts();
        Assert.That(swingsBefore, Is.GreaterThanOrEqualTo(2));
        Assert.That(
            starts.Take(swingsBefore).Select(start => start.Timing.Interval),
            Is.All.EqualTo(TimeSpan.FromMilliseconds(940)));
        Assert.That(starts.Count, Is.GreaterThan(swingsBefore + 1));
        Assert.That(
            starts.Skip(swingsBefore).Select(start => start.Timing.Interval),
            Is.All.EqualTo(TimeSpan.FromMilliseconds(920)));
        AttackTiming focused = starts.Last().Timing;
        Assert.That(
            (focused.Impact, focused.Recovery),
            Is.EqualTo((TimeSpan.FromMilliseconds(460), TimeSpan.FromMilliseconds(230))),
            "the lock and the lunge follow the shorter motion");
        Assert.That(rig.Entity.StatusEffects, Has.Count.EqualTo(1));
    }
}
}
