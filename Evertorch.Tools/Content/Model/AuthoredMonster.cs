using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredMonster
{
    public AuthoredMonster(
        DefinitionSource source,
        MonsterDefinition definition,
        string prefab,
        string icon,
        string? projectile)
    {
        Source = source;
        Definition = definition;
        Prefab = prefab;
        Icon = icon;
        Projectile = projectile;
    }

    public DefinitionSource Source { get; }

    public MonsterDefinition Definition { get; }

    public string Prefab { get; }

    public string Icon { get; }

    /// <summary>What its ranged attacks and casts fly, or null for none (Content Pipeline §6).</summary>
    public string? Projectile { get; }
}
}
