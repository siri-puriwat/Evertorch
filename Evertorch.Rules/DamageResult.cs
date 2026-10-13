namespace Evertorch.Rules
{
public readonly struct DamageResult
{
    public DamageResult(int amount, bool isCritical, bool isImmune = false)
    {
        Amount = amount;
        IsCritical = isCritical;
        IsImmune = isImmune;
    }

    public int Amount { get; }

    public bool IsCritical { get; }

    /// <summary>
    ///     The element table brought the hit to 0 (Gameplay Systems §9): it deals nothing, and its amount is 0.
    /// </summary>
    public bool IsImmune { get; }
}
}
