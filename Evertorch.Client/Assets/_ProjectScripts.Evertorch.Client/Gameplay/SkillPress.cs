namespace Evertorch.Client
{
/// <summary>
///     What a press of a skill's key, bar button, or tap does (Prototype Content §4): a skill for an enemy or an ally
///     waits for a click or tap on its target, and a skill on the caster goes at once.
/// </summary>
public enum SkillPress
{
    /// <summary>A skill on the caster: asked for at once.</summary>
    Instant = 0,

    /// <summary>The press began the choice of a target, or replaced another skill's.</summary>
    Choosing = 1,

    /// <summary>The same enemy skill again: the choice goes on.</summary>
    StillChoosing = 2,

    /// <summary>The same ally skill again: it is for the caster.</summary>
    OnCaster = 3
}
}
