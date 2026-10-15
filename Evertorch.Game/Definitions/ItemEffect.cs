using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     What a consumable does once its use is committed (Gameplay Systems §11.2): flat amounts of HP and SP, each capped
///     at its maximum, a status it starts for a while, and the ailments it ends. Server-only content.
/// </summary>
public sealed class ItemEffect
{
    public ItemEffect(
        int health,
        int spirit,
        StatusDefinitionId? status = null,
        int statusDurationMs = 0,
        IReadOnlyList<Ailment>? cures = null)
    {
        IReadOnlyList<Ailment> ended = SkillEffect.Checked(cures);
        if (health < 0 || spirit < 0 || (health == 0 && spirit == 0 && status == null && ended.Count == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(health),
                "A consumable restores HP or SP, starts a status, or ends an ailment.");
        }

        if ((status != null && statusDurationMs < 1) || (status == null && statusDurationMs != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(statusDurationMs), "A status starts for 1 ms or more.");
        }

        Health = health;
        Spirit = spirit;
        Status = status;
        StatusDurationMs = statusDurationMs;
        Cures = ended;
    }

    public int Health { get; }

    public int Spirit { get; }

    /// <summary>The status a use starts, a converter's (Gameplay Systems §9.1), or null.</summary>
    public StatusDefinitionId? Status { get; }

    public int StatusDurationMs { get; }

    /// <summary>The ailments a use ends, the Remedy's (Gameplay Systems §11.2); empty for none.</summary>
    public IReadOnlyList<Ailment> Cures { get; }
}
}
