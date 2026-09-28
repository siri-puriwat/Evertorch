namespace Evertorch.Game
{
/// <summary>
///     A status effect (Gameplay Systems §9.1): its display name. The level of the skill that applies it names what it
///     adds to the primary statistics and for how long.
/// </summary>
public sealed class StatusEffectDefinition
{
    public StatusEffectDefinition(StatusDefinitionId id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public StatusDefinitionId Id { get; }

    public string DisplayName { get; }
}
}
