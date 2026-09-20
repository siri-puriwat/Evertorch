using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredMonster
{
    public AuthoredMonster(DefinitionSource source, MonsterDefinition definition, string prefab, string icon)
    {
        Source = source;
        Definition = definition;
        Prefab = prefab;
        Icon = icon;
    }

    public DefinitionSource Source { get; }

    public MonsterDefinition Definition { get; }

    public string Prefab { get; }

    public string Icon { get; }
}
}
