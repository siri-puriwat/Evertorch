using System;

namespace Evertorch.Game
{
/// <summary>
///     A status effect (Gameplay Systems §9.1): its display name, the attack element it may set, and the ailment it may
///     be. The level of the skill that applies it names what it adds to the primary statistics and for how long.
/// </summary>
public sealed class StatusEffectDefinition
{
    public StatusEffectDefinition(
        StatusDefinitionId id,
        string displayName,
        Element? attackElement = null,
        Ailment ailment = Ailment.None,
        int durationMs = 0)
    {
        if (ailment > Ailment.Blind)
        {
            throw new ArgumentOutOfRangeException(nameof(ailment), ailment, "An ailment is one of the six.");
        }

        if (ailment == Ailment.None != (durationMs == 0)
            || durationMs < 0
            || durationMs > ContentLimits.MaxDurationMs
            || (ailment != Ailment.None && attackElement != null))
        {
            throw new ArgumentException(
                "An ailment's status alone names a duration, and sets no attack element.",
                nameof(durationMs));
        }

        Id = id;
        DisplayName = displayName;
        AttackElement = attackElement;
        Ailment = ailment;
        DurationMs = durationMs;
    }

    public StatusDefinitionId Id { get; }

    public string DisplayName { get; }

    /// <summary>
    ///     The element the wearer's swings and weapon skills strike with while it lasts, a converter's; null for a status
    ///     that sets none. Server-only content.
    /// </summary>
    public Element? AttackElement { get; }

    /// <summary>The ailment this status is, None for any other. Server-only content.</summary>
    public Ailment Ailment { get; }

    /// <summary>
    ///     How long the ailment lasts before resistance when a worn card lands it, in ms; 0 for a status that is no
    ///     ailment. Server-only content.
    /// </summary>
    public int DurationMs { get; }
}
}
