namespace Evertorch.Rules
{
public readonly struct DamageResult
{
    public DamageResult(int amount, bool isCritical)
    {
        Amount = amount;
        IsCritical = isCritical;
    }

    public int Amount { get; }

    public bool IsCritical { get; }
}
}
