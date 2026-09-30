namespace Evertorch.Client
{
/// <summary>
///     What a body is doing this frame, on its own timeline (Gameplay Systems §8): the combat presenter gathers it from
///     the <see cref="CombatTimeline" />, and <see cref="BodyClipChoice" /> turns it into a clip and a time. Every
///     "since" is in seconds from its moment; a negative one has not come yet.
/// </summary>
public struct BodyCue
{
    public bool IsDead;

    /// <summary>
    ///     Seconds since the death was shown; infinite for a body first seen dead.
    /// </summary>
    public double DeathSince;

    public bool HasSwing;
    public double SwingSince;

    /// <summary>
    ///     The swing's impact, in seconds after its start.
    /// </summary>
    public double Impact;

    public bool HasSkill;
    public double SkillSince;

    /// <summary>
    ///     The skill's cast time; 0 for an instant skill.
    /// </summary>
    public double CastSeconds;

    /// <summary>
    ///     Whether the skill is used on an enemy, from the client content's target type.
    /// </summary>
    public bool IsEnemySkill;

    public bool HasProjectile;

    public bool HasHit;
    public double HitSince;

    /// <summary>
    ///     The body's horizontal speed over the ground, in metres a second.
    /// </summary>
    public float Speed;

    /// <summary>
    ///     The distance the body has walked, in metres, which sets the movement cycle's phase.
    /// </summary>
    public double Travelled;

    /// <summary>
    ///     Real seconds, for the loops that follow no server moment.
    /// </summary>
    public double Clock;

    /// <summary>
    ///     The attack clip its weapon's grip names: <c>attack_sword</c>, <c>attack_staff</c>, or
    ///     <c>attack_unarmed</c>.
    /// </summary>
    public string AttackClip;

    public bool IsTalking;
}
}
