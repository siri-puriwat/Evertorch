using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Which clip a body plays and where in it (Gameplay Systems §8), from the server's timing alone.
/// </summary>
[TestFixture]
public sealed class BodyClipChoiceTests
{
    [SetUp]
    public void CreateClips()
    {
        m_humanoid = Clips(
            ("idle", 60, true, null, 0f),
            ("run", 20, true, null, 5f),
            ("attack_unarmed", 36, false, ("impact", 24), 0f),
            ("attack_sword", 36, false, ("impact", 24), 0f),
            ("skill_melee", 15, false, ("impact", 1), 0f),
            ("cast_loop", 30, true, null, 0f),
            ("cast_release", 15, false, ("release", 2), 0f),
            ("buff", 15, false, ("apply", 1), 0f),
            ("hit", 12, false, null, 0f),
            ("death", 36, false, null, 0f),
            ("talk", 60, true, null, 0f));
    }

    [TearDown]
    public void DestroyClips()
    {
        foreach (Object created in m_created)
        {
            Object.DestroyImmediate(created);
        }

        m_created.Clear();
    }

    private readonly List<Object> m_created = new();
    private BodyClips m_humanoid = null!;

    private BodyClips Clips(params (string Name, int Frames, bool Loop, (string, int)? Marker, float Speed)[] entries)
    {
        BodyClips clips = ScriptableObject.CreateInstance<BodyClips>();
        m_created.Add(clips);
        var built = new List<BodyClips.Entry>();
        foreach ((string name, int frames, bool loop, (string, int)? marker, float speed) in entries)
        {
            var clip = new AnimationClip { name = name };
            m_created.Add(clip);
            BodyClips.Marker[] markers = marker == null
                ? new BodyClips.Marker[0]
                : new[] { new BodyClips.Marker(marker.Value.Item1, marker.Value.Item2) };
            built.Add(new BodyClips.Entry(name, clip, frames, loop, markers, speed));
        }

        clips.Configure("test", built.ToArray());
        return clips;
    }

    private static BodyClipPick Choose(BodyCue cue, BodyClips clips)
    {
        Assert.That(BodyClipChoice.TryChoose(cue, clips, out BodyClipPick pick), Is.True);
        return pick;
    }

    private static BodyCue Swing(double since, double impact)
    {
        return new BodyCue
        {
            HasSwing = true,
            SwingSince = since,
            Impact = impact,
            AttackClip = BodyClipChoice.AttackSword
        };
    }

    [TestCase(0.47)]
    [TestCase(0.1)]
    [TestCase(1.0)]
    public void Choose_AtASwingsImpact_ShowsTheImpactFrame(double impact)
    {
        BodyClipPick pick = Choose(Swing(impact, impact), m_humanoid);

        Assert.That(pick.Clip, Is.EqualTo("attack_sword"));
        Assert.That(pick.Time * BodyClips.FramesPerSecond, Is.EqualTo(24.0).Within(0.0001));
    }

    [TestCase(true, "skill_melee")]
    [TestCase(false, "buff")]
    public void Choose_ForAnInstantSkill_StartsAtItsMarker(bool isEnemySkill, string expected)
    {
        var cue = new BodyCue { HasSkill = true, SkillSince = 0.0, IsEnemySkill = isEnemySkill };

        BodyClipPick pick = Choose(cue, m_humanoid);

        Assert.That(pick.Clip, Is.EqualTo(expected));
        Assert.That(pick.Time * 30.0, Is.EqualTo(1.0).Within(0.0001), "frame 1 at the moment it is shown");
    }

    [Test]
    public void Choose_AtTheFastestSwing_FadesInWithinAQuarterOfTheImpact()
    {
        BodyClipPick pick = Choose(Swing(0.0, 0.1), m_humanoid);

        Assert.That(pick.FadeSeconds, Is.EqualTo(0.025).Within(0.0001));
    }

    [Test]
    public void Choose_ForACastWithAProjectile_ReleasesAsTheProjectileLeaves()
    {
        var cue = new BodyCue
        {
            HasSkill = true,
            CastSeconds = 1.1,
            IsEnemySkill = true,
            HasProjectile = true,
            SkillSince = 0.7
        };

        BodyClipPick pick = Choose(cue, m_humanoid);

        Assert.That(pick.Clip, Is.EqualTo("cast_release"));
        Assert.That(pick.Time * 30.0, Is.EqualTo(2.0).Within(0.0001), "0.4 s before the resolution");
    }

    [Test]
    public void Choose_ForACast_LoopsThenReleasesAtTheResolution()
    {
        var cue = new BodyCue { HasSkill = true, CastSeconds = 1.0, IsEnemySkill = false };

        cue.SkillSince = 0.5;
        Assert.That(Choose(cue, m_humanoid).Clip, Is.EqualTo("cast_loop"));
        cue.SkillSince = 1.0;
        BodyClipPick release = Choose(cue, m_humanoid);
        Assert.That(release.Clip, Is.EqualTo("cast_release"));
        Assert.That(release.Time * 30.0, Is.EqualTo(2.0).Within(0.0001), "its release frame at the resolution");
    }

    [Test]
    public void Choose_ForADeath_HoldsItsLastFrame_EvenMidSwing()
    {
        BodyCue cue = Swing(0.2, 0.47);
        cue.IsDead = true;
        cue.DeathSince = 5.0;

        BodyClipPick pick = Choose(cue, m_humanoid);

        Assert.That((pick.Clip, pick.Time), Is.EqualTo(("death", 36.0 / 30.0)));
        cue.DeathSince = double.PositiveInfinity;
        Assert.That(Choose(cue, m_humanoid).Time, Is.EqualTo(36.0 / 30.0), "first seen dead: already lying there");
    }

    [Test]
    public void Choose_ForAGripTheRigLacks_SwingsItsOwnAttack()
    {
        BodyClips slime = Clips(("idle", 60, true, null, 0f), ("attack", 36, false, ("impact", 24), 0f));

        BodyClipPick pick = Choose(Swing(0.6, 0.6), slime);

        Assert.That(pick.Clip, Is.EqualTo("attack"));
        Assert.That(pick.Time * 30.0, Is.EqualTo(24.0).Within(0.0001));
    }

    [Test]
    public void Choose_ForAHitWhileStanding_FlinchesUntilTheClipEnds()
    {
        var cue = new BodyCue { HasHit = true, HitSince = 0.1 };

        Assert.That(Choose(cue, m_humanoid).Clip, Is.EqualTo("hit"));
        cue.HitSince = 0.5;
        Assert.That(Choose(cue, m_humanoid).Clip, Is.EqualTo("idle"));
    }

    [Test]
    public void Choose_ForASwingsRecovery_EndsTheClipBeforeTheNextSwing()
    {
        const double impact = 0.47;

        Assert.That(Choose(Swing(impact * 1.49, impact), m_humanoid).Clip, Is.EqualTo("attack_sword"));
        Assert.That(Choose(Swing(impact * 1.51, impact), m_humanoid).Clip, Is.EqualTo("idle"), "the clip is over");
        Assert.That(Choose(Swing(-0.01, impact), m_humanoid).Clip, Is.EqualTo("idle"), "the swing has not begun");
    }

    [Test]
    public void Choose_ForAWalk_FollowsTheGroundCovered()
    {
        // One run cycle covers 5 m/s × 20/30 s = 3.33 m; half of it is half the clip.
        var cue = new BodyCue { Speed = 5f, Travelled = 5.0 / 3.0 };

        BodyClipPick pick = Choose(cue, m_humanoid);

        Assert.That(pick.Clip, Is.EqualTo("run"));
        Assert.That(pick.Time, Is.EqualTo(10.0 / 30.0).Within(0.0001));
    }

    [Test]
    public void Choose_ForAnNpcWhoseWindowShows_Talks()
    {
        Assert.That(Choose(new BodyCue { IsTalking = true, Clock = 1.0 }, m_humanoid).Clip, Is.EqualTo("talk"));
        Assert.That(Choose(new BodyCue { Clock = 1.0 }, m_humanoid).Clip, Is.EqualTo("idle"));
    }

    [Test]
    public void Choose_WhenABodyWalksOffAfterTheImpact_ShowsTheWalk()
    {
        BodyCue during = Swing(0.4, 0.47);
        during.Speed = 5f;
        BodyCue after = Swing(0.5, 0.47);
        after.Speed = 5f;

        Assert.That(Choose(during, m_humanoid).Clip, Is.EqualTo("attack_sword"), "held still until the impact");
        Assert.That(Choose(after, m_humanoid).Clip, Is.EqualTo("run"));
    }
}
}
