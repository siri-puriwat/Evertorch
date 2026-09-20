using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredMap
{
    public AuthoredMap(DefinitionSource source, MapDefinition definition, string scene)
    {
        Source = source;
        Definition = definition;
        Scene = scene;
    }

    public DefinitionSource Source { get; }

    public MapDefinition Definition { get; }

    public string Scene { get; }
}
}
