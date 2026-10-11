using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredRefineTable
{
    public AuthoredRefineTable(DefinitionSource source, RefineTableDefinition definition)
    {
        Source = source;
        Definition = definition;
    }

    public DefinitionSource Source { get; }

    public RefineTableDefinition Definition { get; }
}
}
