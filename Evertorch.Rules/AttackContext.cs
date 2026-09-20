using System;

namespace Evertorch.Rules
{
/// <summary>
/// What sets the pace of a basic attack: the attack speed of a character, or the fixed interval from a monster
/// definition.
/// </summary>
public readonly struct AttackContext
{
    private AttackContext(int attackSpeed, TimeSpan fixedInterval, bool hasFixedInterval)
    {
        AttackSpeed = attackSpeed;
        FixedInterval = fixedInterval;
        HasFixedInterval = hasFixedInterval;
    }

    public int AttackSpeed { get; }

    public TimeSpan FixedInterval { get; }

    public bool HasFixedInterval { get; }

    public static AttackContext ForAttackSpeed(int attackSpeed)
    {
        if (attackSpeed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attackSpeed), "Attack speed cannot be negative.");
        }

        return new AttackContext(attackSpeed, TimeSpan.Zero, false);
    }

    public static AttackContext ForFixedInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The attack interval must be positive.");
        }

        return new AttackContext(0, interval, true);
    }
}
}
