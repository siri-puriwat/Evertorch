using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredExperienceTable
{
    public AuthoredExperienceTable(DefinitionSource source, ExperienceTableDefinition definition)
    {
        Source = source;
        Definition = definition;
    }

    public DefinitionSource Source { get; }

    public ExperienceTableDefinition Definition { get; }
}
}
