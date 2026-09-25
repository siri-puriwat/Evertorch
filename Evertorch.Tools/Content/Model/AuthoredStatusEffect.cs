using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredStatusEffect
{
    public AuthoredStatusEffect(DefinitionSource source, StatusEffectDefinition definition, string? icon)
    {
        Source = source;
        Definition = definition;
        Icon = icon;
    }

    public DefinitionSource Source { get; }

    public StatusEffectDefinition Definition { get; }

    /// <summary>
    ///     The optional logical key of the effect's icon; null when none is authored.
    /// </summary>
    public string? Icon { get; }
}
}
