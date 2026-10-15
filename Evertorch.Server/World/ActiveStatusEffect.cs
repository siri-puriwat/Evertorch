using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     A status effect on a player or a monster: what it adds to the primary statistics, the attack element it sets, the
///     ailment it is, when it ends, and, for poison, when its next tick falls due.
/// </summary>
public readonly struct ActiveStatusEffect
{
    public ActiveStatusEffect(
        StatusDefinitionId status,
        StatPercentages statPercent,
        long endMs,
        Element? attackElement = null,
        Ailment ailment = Ailment.None,
        long nextTickMs = long.MaxValue)
    {
        Status = status;
        StatPercent = statPercent;
        EndMs = endMs;
        AttackElement = attackElement;
        Ailment = ailment;
        NextTickMs = nextTickMs;
    }

    public StatusDefinitionId Status { get; }

    public StatPercentages StatPercent { get; }

    public long EndMs { get; }

    /// <summary>The element a converter's status gives the player's swings and weapon skills; null for any other.</summary>
    public Element? AttackElement { get; }

    /// <summary>The ailment the status is (Gameplay Systems §9.1); None for any other.</summary>
    public Ailment Ailment { get; }

    /// <summary>
    ///     When poison next drains, every 1,000 ms from its start; <see cref="long.MaxValue" /> for every other status.
    /// </summary>
    public long NextTickMs { get; }

    public ActiveStatusEffect WithNextTick(long nextTickMs)
    {
        return new ActiveStatusEffect(Status, StatPercent, EndMs, AttackElement, Ailment, nextTickMs);
    }
}
}
