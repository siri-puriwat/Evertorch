namespace Evertorch.Game
{
public sealed class MonsterDrop
{
    public MonsterDrop(ItemDefinitionId item, double chance, int minAmount, int maxAmount)
    {
        Item = item;
        Chance = chance;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
    }

    public ItemDefinitionId Item { get; }

    /// <summary>
    /// Probability from 0 to 1 inclusive. The server rolls r in [0, 1) and drops when r is below the chance,
    /// so 0 never drops and 1 always drops.
    /// </summary>
    public double Chance { get; }

    public int MinAmount { get; }

    public int MaxAmount { get; }
}
}
