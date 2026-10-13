namespace Evertorch.Game
{
/// <summary>
///     A status effect (Gameplay Systems §9.1): its display name, and the attack element it may set. The level of the
///     skill that applies it names what it adds to the primary statistics and for how long.
/// </summary>
public sealed class StatusEffectDefinition
{
    public StatusEffectDefinition(StatusDefinitionId id, string displayName, Element? attackElement = null)
    {
        Id = id;
        DisplayName = displayName;
        AttackElement = attackElement;
    }

    public StatusDefinitionId Id { get; }

    public string DisplayName { get; }

    /// <summary>
    ///     The element the wearer's swings and weapon skills strike with while it lasts, a converter's; null for a status
    ///     that sets none. Server-only content.
    /// </summary>
    public Element? AttackElement { get; }
}
}
