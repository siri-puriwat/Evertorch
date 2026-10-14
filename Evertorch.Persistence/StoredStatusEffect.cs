using System;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     A status effect a character had at its last checkpoint: its status, the time it had left, which does not run
///     while the character is offline, and what it adds to the primary statistics (Persistence §6). The status's ID is
///     the stored text, which the current content may no longer define; the load drops such a status (Persistence §8).
/// </summary>
public sealed class StoredStatusEffect
{
    public StoredStatusEffect(string statusDefinitionId, int remainingMs, StatPercentages statPercent)
    {
        if (remainingMs < 1 || remainingMs > ContentLimits.MaxDurationMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingMs),
                $"A stored effect has 1 to {ContentLimits.MaxDurationMs} ms left.");
        }

        StatusDefinitionId = statusDefinitionId ?? throw new ArgumentNullException(nameof(statusDefinitionId));
        RemainingMs = remainingMs;
        StatPercent = statPercent;
    }

    public string StatusDefinitionId { get; }

    /// <summary>
    ///     What was left of the effect when its checkpoint was built, from 1 ms to a day.
    /// </summary>
    public int RemainingMs { get; }

    public StatPercentages StatPercent { get; }
}
}
