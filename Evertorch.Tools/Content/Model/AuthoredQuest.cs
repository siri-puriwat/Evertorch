using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredQuest
{
    public AuthoredQuest(DefinitionSource source, QuestDefinition definition)
    {
        Source = source;
        Definition = definition;
    }

    public DefinitionSource Source { get; }

    public QuestDefinition Definition { get; }
}
}
