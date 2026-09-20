namespace Evertorch.Rules
{
public readonly struct HitResult
{
    public HitResult(HitOutcome outcome, int hitChancePercent)
    {
        Outcome = outcome;
        HitChancePercent = hitChancePercent;
    }

    public HitOutcome Outcome { get; }

    /// <summary>The clamped chance the ordinary hit roll used, reported for telemetry and tests.</summary>
    public int HitChancePercent { get; }

    public bool DealsDamage => Outcome == HitOutcome.Hit || Outcome == HitOutcome.Critical;
}
}
