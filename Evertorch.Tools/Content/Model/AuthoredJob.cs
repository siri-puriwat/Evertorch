using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredJob
{
    public AuthoredJob(DefinitionSource source, JobDefinition definition, string prefab)
    {
        Source = source;
        Definition = definition;
        Prefab = prefab;
    }

    public DefinitionSource Source { get; }

    public JobDefinition Definition { get; }

    public string Prefab { get; }
}
}
