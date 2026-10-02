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
        string? projectile,
        double? scale = null,
        string? tint = null)
    {
        Source = source;
        Definition = definition;
        Prefab = prefab;
        Icon = icon;
        Projectile = projectile;
        Scale = scale;
        Tint = tint;
    }

    public DefinitionSource Source { get; }

    public MonsterDefinition Definition { get; }

    public string Prefab { get; }

    public string Icon { get; }

    /// <summary>What its ranged attacks and casts fly, or null for none (Content Pipeline §6).</summary>
    public string? Projectile { get; }

    /// <summary>How large its body is drawn against its model's own size, or null when not authored (1).</summary>
    public double? Scale { get; }

    /// <summary>The colour its whole body is drawn in, "#RRGGBB", or null for the model's own colours.</summary>
    public string? Tint { get; }
}
}
