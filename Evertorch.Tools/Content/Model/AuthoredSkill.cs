using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredSkill
{
    public AuthoredSkill(DefinitionSource source, SkillDefinition definition, string icon, string? description = null)
    {
        Source = source;
        Definition = definition;
        Icon = icon;
        Description = description;
    }

    public DefinitionSource Source { get; }

    public SkillDefinition Definition { get; }

    public string Icon { get; }

    /// <summary>
    ///     What the player reads about the skill in the Skills window (Content Pipeline §5); null when none is written.
    /// </summary>
    public string? Description { get; }
}
}
