namespace Evertorch.Game
{
/// <summary>
/// Statistics the rules derive from a character build. Never stored; recalculated when an input changes.
/// </summary>
public sealed class DerivedStats
{
    public DerivedStats(
        int maxHp,
        int maxSp,
        int physicalAttack,
        int magicalAttack,
        int softDefense,
        int softMagicDefense,
        int hit,
        int flee,
        int critical,
        int perfectDodge,
        int attackSpeed,
        float movementSpeed,
        int variableCastPermille,
        int fixedCastPermille)
    {
        MaxHp = maxHp;
        MaxSp = maxSp;
        PhysicalAttack = physicalAttack;
        MagicalAttack = magicalAttack;
        SoftDefense = softDefense;
        SoftMagicDefense = softMagicDefense;
        Hit = hit;
        Flee = flee;
        Critical = critical;
        PerfectDodge = perfectDodge;
        AttackSpeed = attackSpeed;
        MovementSpeed = movementSpeed;
        VariableCastPermille = variableCastPermille;
        FixedCastPermille = fixedCastPermille;
    }

    public int MaxHp { get; }

    public int MaxSp { get; }

    public int PhysicalAttack { get; }

    public int MagicalAttack { get; }

    public int SoftDefense { get; }

    public int SoftMagicDefense { get; }

    public int Hit { get; }

    public int Flee { get; }

    /// <summary>Critical chance in tenths of a percent.</summary>
    public int Critical { get; }

    /// <summary>Perfect-dodge chance in tenths of a percent.</summary>
    public int PerfectDodge { get; }

    public int AttackSpeed { get; }

    /// <summary>World units per second.</summary>
    public float MovementSpeed { get; }

    /// <summary>Share of a skill's variable cast time that remains, in thousandths.</summary>
    public int VariableCastPermille { get; }

    /// <summary>Share of a skill's fixed cast time that remains, in thousandths.</summary>
    public int FixedCastPermille { get; }
}
}
