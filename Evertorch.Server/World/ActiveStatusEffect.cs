using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     A status effect on a player: what it adds to the primary statistics, and when it ends.
/// </summary>
public readonly struct ActiveStatusEffect
{
    public ActiveStatusEffect(StatusDefinitionId status, StatPercentages statPercent, long endMs)
    {
        Status = status;
        StatPercent = statPercent;
        EndMs = endMs;
    }

    public StatusDefinitionId Status { get; }

    public StatPercentages StatPercent { get; }

    public long EndMs { get; }
}
}
