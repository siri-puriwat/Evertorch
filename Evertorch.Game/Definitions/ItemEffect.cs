using System;

namespace Evertorch.Game
{
/// <summary>
///     What a consumable does once its use is committed (Gameplay Systems §11.2): flat amounts of HP and SP, each capped
///     at its maximum, and a status it starts for a while. Server-only content.
/// </summary>
public sealed class ItemEffect
{
    public ItemEffect(int health, int spirit, StatusDefinitionId? status = null, int statusDurationMs = 0)
    {
        if (health < 0 || spirit < 0 || (health == 0 && spirit == 0 && status == null))
        {
            throw new ArgumentOutOfRangeException(nameof(health),
                "A consumable restores HP or SP, or starts a status.");
        }

        if ((status != null && statusDurationMs < 1) || (status == null && statusDurationMs != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(statusDurationMs), "A status starts for 1 ms or more.");
        }

        Health = health;
        Spirit = spirit;
        Status = status;
        StatusDurationMs = statusDurationMs;
    }

    public int Health { get; }

    public int Spirit { get; }

    /// <summary>The status a use starts, a converter's (Gameplay Systems §9.1), or null.</summary>
    public StatusDefinitionId? Status { get; }

    public int StatusDurationMs { get; }
}
}
