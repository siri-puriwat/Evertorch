namespace Evertorch.Game
{
/// <summary>
///     A status effect (Gameplay Systems §9.1): its display name and the percentages it adds to the primary
///     statistics while it lasts. The skill that applies it names its duration.
/// </summary>
public sealed class StatusEffectDefinition
{
    public StatusEffectDefinition(StatusDefinitionId id, string displayName, StatPercentages statPercent)
    {
        Id = id;
        DisplayName = displayName;
        StatPercent = statPercent;
    }

    public StatusDefinitionId Id { get; }

    public string DisplayName { get; }

    public StatPercentages StatPercent { get; }
}
}
