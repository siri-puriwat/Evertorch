using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     A status effect on a player: what it adds to the primary statistics, the attack element it sets, and when it
///     ends.
/// </summary>
public readonly struct ActiveStatusEffect
{
    public ActiveStatusEffect(
        StatusDefinitionId status,
        StatPercentages statPercent,
        long endMs,
        Element? attackElement = null)
    {
        Status = status;
        StatPercent = statPercent;
        EndMs = endMs;
        AttackElement = attackElement;
    }

    public StatusDefinitionId Status { get; }

    public StatPercentages StatPercent { get; }

    public long EndMs { get; }

    /// <summary>The element a converter's status gives the player's swings and weapon skills; null for any other.</summary>
    public Element? AttackElement { get; }
}
}
