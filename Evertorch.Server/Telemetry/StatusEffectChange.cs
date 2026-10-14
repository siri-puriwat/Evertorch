namespace Evertorch.Server
{
/// <summary>
///     What happened to a status effect, as <c>evertorch.combat.status_effects</c> counts it.
/// </summary>
public enum StatusEffectChange
{
    Started,
    Renewed,
    Ended,

    /// <summary>Restored from the character's checkpoint at a load (Persistence §7).</summary>
    Restored,

    /// <summary>Dropped at a load: a converter without a worn weapon, or a status the content lacks.</summary>
    Dropped
}
}
