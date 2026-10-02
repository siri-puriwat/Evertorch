using System;

namespace Evertorch.Client
{
/// <summary>
///     Chooses which clip a body plays and where in it (Gameplay Systems §8), from what the server's timing says the
///     body is doing. The choice depends only on the cue, so a body is drawn the same at any frame rate, and nothing
///     it picks is ever read back into play.
/// </summary>
public static class BodyClipChoice
{
    public const string Idle = "idle";
    public const string Run = "run";
    public const string Move = "move";
    public const string Talk = "talk";
    public const string Hit = "hit";
    public const string Death = "death";
    public const string MonsterAttack = "attack";
    public const string AttackUnarmed = "attack_unarmed";
    public const string AttackSword = "attack_sword";
    public const string AttackStaff = "attack_staff";
    public const string CastLoop = "cast_loop";
    public const string CastRelease = "cast_release";
    public const string SkillMelee = "skill_melee";
    public const string Buff = "buff";
    public const string ImpactMarker = "impact";
    public const string ReleaseMarker = "release";
    public const string ApplyMarker = "apply";

    /// <summary>
    ///     How long before its resolution a projectile leaves, as the projectile presenter flies it (Gameplay Systems
    ///     §8).
    /// </summary>
    public const double ProjectileFlightSeconds = 0.4;

    // Slower than this, a body stands; the walk's stop settles below it within a frame or two.
    private const float MovingSpeed = 0.2f;

    private const double LongestFade = 0.1;

    /// <summary>
    ///     The clip and the time in it for this frame, and how long a change to it should fade in; false when the rig
    ///     has none of the clips the cue could use.
    /// </summary>
    public static bool TryChoose(in BodyCue cue, BodyClips clips, out BodyClipPick pick)
    {
        pick = default;
        if (cue.IsDead)
        {
            return TryHeld(clips, Death, cue.DeathSince, LongestFade, out pick) ||
                TryLoop(clips, Idle, cue.Clock, out pick);
        }

        bool isSkillLatest = cue.HasSkill && (!cue.HasSwing || cue.SkillSince <= cue.SwingSince);
        if (isSkillLatest && TrySkill(cue, clips, out pick))
        {
            return true;
        }

        if (cue.HasSwing && TrySwing(cue, clips, out pick))
        {
            return true;
        }

        if (!isSkillLatest && cue.HasSkill && TrySkill(cue, clips, out pick))
        {
            return true;
        }

        bool isMoving = cue.Speed > MovingSpeed;
        if (!isMoving && cue.HasHit && cue.HitSince >= 0.0 && TryOnce(clips, Hit, cue.HitSince, out pick))
        {
            return true;
        }

        double bodyScale = cue.BodyScale > 0f ? cue.BodyScale : 1.0;
        if (isMoving
            && (TryCycle(clips, Run, cue.Travelled, bodyScale, out pick)
                || TryCycle(clips, Move, cue.Travelled, bodyScale, out pick)))
        {
            return true;
        }

        return (cue.IsTalking && TryLoop(clips, Talk, cue.Clock, out pick)) ||
            TryLoop(clips, Idle, cue.Clock, out pick);
    }

    // A swing's clip lands its impact marker on the server's impact: one scale set by the impact carries the rest,
    // and the recovery (half a motion) ends before the next swing (Gameplay Systems §7). After the impact the body may
    // walk, and then its walk takes over.
    private static bool TrySwing(in BodyCue cue, BodyClips clips, out BodyClipPick pick)
    {
        pick = default;
        if (cue.SwingSince < 0.0 || cue.Impact <= 0.0)
        {
            return false;
        }

        string name = clips.TryGet(cue.AttackClip, out _) ? cue.AttackClip : MonsterAttack;
        if (!clips.TryGet(name, out BodyClips.Entry entry) || !entry.TryGetMarker(ImpactMarker, out int impactFrame))
        {
            return false;
        }

        double scale = impactFrame / (double)BodyClips.FramesPerSecond / cue.Impact;
        double time = cue.SwingSince * scale;
        if (time >= entry.Seconds || (cue.SwingSince > cue.Impact && cue.Speed > MovingSpeed))
        {
            return false;
        }

        pick = new BodyClipPick(name, time, Math.Min(LongestFade, cue.Impact / 4.0));
        return true;
    }

    /// <summary>
    ///     Where in a swing the body's attack clip lets its projectile go: its <c>release</c> marker's share of the way
    ///     to its <c>impact</c>, since the swing is scaled to put the impact on the server's (Gameplay Systems §8); false
    ///     when the clip marks no release.
    /// </summary>
    public static bool TryGetAttackRelease(BodyClips clips, string attackClip, out double share)
    {
        share = 0.0;
        string name = clips.TryGet(attackClip, out _) ? attackClip : MonsterAttack;
        if (!clips.TryGet(name, out BodyClips.Entry entry)
            || !entry.TryGetMarker(ReleaseMarker, out int release)
            || !entry.TryGetMarker(ImpactMarker, out int impact)
            || release < 0
            || release >= impact)
        {
            return false;
        }

        share = release / (double)impact;
        return true;
    }

    // A cast loops until its release, whose marker lands on the resolution, or on the launch of its projectile; an
    // instant skill starts at its marker, the moment it is shown.
    private static bool TrySkill(in BodyCue cue, BodyClips clips, out BodyClipPick pick)
    {
        pick = default;
        if (cue.SkillSince < 0.0)
        {
            return false;
        }

        if (cue.CastSeconds <= 0.0)
        {
            string instant = cue.IsEnemySkill ? SkillMelee : Buff;
            string marker = cue.IsEnemySkill ? ImpactMarker : ApplyMarker;
            return TryFromMarker(clips, instant, marker, cue.SkillSince, out pick);
        }

        double release = cue.HasProjectile
            ? Math.Max(0.0, cue.CastSeconds - ProjectileFlightSeconds)
            : cue.CastSeconds;
        if (TryFromMarker(clips, CastRelease, ReleaseMarker, cue.SkillSince - release, out pick))
        {
            return true;
        }

        return cue.SkillSince < release && TryLoop(clips, CastLoop, cue.SkillSince, out pick);
    }

    private static bool TryFromMarker(BodyClips clips, string name, string marker, double since, out BodyClipPick pick)
    {
        pick = default;
        if (!clips.TryGet(name, out BodyClips.Entry entry))
        {
            return false;
        }

        entry.TryGetMarker(marker, out int frame);
        double time = since + frame / (double)BodyClips.FramesPerSecond;
        if (time < 0.0 || time >= entry.Seconds)
        {
            return false;
        }

        pick = new BodyClipPick(name, time, LongestFade);
        return true;
    }

    private static bool TryOnce(BodyClips clips, string name, double since, out BodyClipPick pick)
    {
        pick = default;
        if (!clips.TryGet(name, out BodyClips.Entry entry) || since >= entry.Seconds)
        {
            return false;
        }

        pick = new BodyClipPick(name, since, LongestFade);
        return true;
    }

    // A clip that ends in a pose to keep, such as a death, holds its last frame.
    private static bool TryHeld(BodyClips clips, string name, double since, double fade, out BodyClipPick pick)
    {
        pick = default;
        if (!clips.TryGet(name, out BodyClips.Entry entry))
        {
            return false;
        }

        pick = new BodyClipPick(name, Math.Min(Math.Max(since, 0.0), entry.Seconds), fade);
        return true;
    }

    private static bool TryLoop(BodyClips clips, string name, double seconds, out BodyClipPick pick)
    {
        pick = default;
        if (!clips.TryGet(name, out BodyClips.Entry entry) || entry.Seconds <= 0.0)
        {
            return false;
        }

        pick = new BodyClipPick(name, Repeat(seconds, entry.Seconds), LongestFade);
        return true;
    }

    // A movement cycle's phase follows the ground covered, so its planted feet keep pace at any speed; a body drawn
    // larger takes longer strides.
    private static bool TryCycle(
        BodyClips clips,
        string name,
        double travelled,
        double bodyScale,
        out BodyClipPick pick)
    {
        pick = default;
        if (!clips.TryGet(name, out BodyClips.Entry entry) || entry.Seconds <= 0.0)
        {
            return false;
        }

        double stride = entry.CalibrationSpeed > 0f ? entry.CalibrationSpeed * entry.Seconds * bodyScale : 0.0;
        double cycles = stride > 0.0 ? travelled / stride : 0.0;
        pick = new BodyClipPick(name, Repeat(cycles, 1.0) * entry.Seconds, LongestFade);
        return true;
    }

    private static double Repeat(double value, double length)
    {
        double result = value % length;
        return result < 0.0 ? result + length : result;
    }
}

/// <summary>
///     A clip by name, the time in it, and how long a change to it fades in.
/// </summary>
public readonly struct BodyClipPick
{
    public BodyClipPick(string clip, double time, double fadeSeconds)
    {
        Clip = clip;
        Time = time;
        FadeSeconds = fadeSeconds;
    }

    public string Clip { get; }

    public double Time { get; }

    public double FadeSeconds { get; }
}
}
