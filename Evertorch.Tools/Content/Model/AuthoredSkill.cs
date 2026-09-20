using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredSkill
{
    public AuthoredSkill(DefinitionSource source, SkillDefinition definition, string icon)
    {
        Source = source;
        Definition = definition;
        Icon = icon;
    }

    public DefinitionSource Source { get; }

    public SkillDefinition Definition { get; }

    public string Icon { get; }
}
}
