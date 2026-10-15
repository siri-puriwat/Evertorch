using System;

namespace Evertorch.Game
{
/// <summary>
///     The ailment a damage skill's landed hit may also bring (Gameplay Systems §9.1): the ailment's status, its chance
///     before resistance in basis points, and how long it lasts before resistance. Server-only content.
/// </summary>
public sealed class AilmentRider
{
    public AilmentRider(StatusDefinitionId status, int chanceBasisPoints, int durationMs)
    {
        if (status == default)
        {
            throw new ArgumentException("A rider names its ailment's status.", nameof(status));
        }

        if (chanceBasisPoints < 1 || chanceBasisPoints > ContentLimits.MaxChanceBasisPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(chanceBasisPoints), chanceBasisPoints,
                "A rider's chance is 1 to 10,000 basis points.");
        }

        if (durationMs < 1 || durationMs > ContentLimits.MaxDurationMs)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), durationMs, "A rider's ailment must last.");
        }

        Status = status;
        ChanceBasisPoints = chanceBasisPoints;
        DurationMs = durationMs;
    }

    public StatusDefinitionId Status { get; }

    /// <summary>The chance it lands before resistance; 10,000 is certain.</summary>
    public int ChanceBasisPoints { get; }

    public int DurationMs { get; }
}
}
