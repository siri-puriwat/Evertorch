namespace Evertorch.Game
{
/// <summary>
///     One prize of a boss's most valuable player (Gameplay Systems §10): an item, the chance it is kept, and how many.
///     A boss rolls its prizes in order and keeps the first that succeeds.
/// </summary>
public sealed class MvpDrop
{
    public MvpDrop(ItemDefinitionId item, double chance, int amount)
    {
        Item = item;
        Chance = chance;
        Amount = amount;
    }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     Probability from 0 to 1 inclusive, rolled as a drop's chance is.
    /// </summary>
    public double Chance { get; }

    public int Amount { get; }
}
}
