namespace Evertorch.Tools
{
/// <summary>
///     Shared numeric bounds for canonical content. They reject nonsense; tuning stays a design decision.
/// </summary>
internal static class ContentLimits
{
    public const int MaxLevel = 999;
    public const int MaxStat = 9999;
    public const int MaxHp = 100_000_000;
    public const int MaxStack = 1_000_000;
    public const int MaxPrice = 1_000_000_000;
    public const int MaxDurationMs = 86_400_000;
    public const int MaxSpawnCount = 1000;
    public const double MaxDistance = 10_000d;
    public const double MaxCoordinate = 100_000d;
    public const double MaxSpeed = 100d;
    public const double MaxCellSize = 100d;
    public const double MaxAgentRadius = 10d;
    public const double MaxStepHeight = 100d;

    // Generated packages name each distinct cell kind with one letter.
    public const int MaxLegendEntries = 52;
}
}
