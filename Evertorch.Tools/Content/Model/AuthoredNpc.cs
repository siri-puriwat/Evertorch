using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredNpc
{
    public AuthoredNpc(DefinitionSource source, NpcDefinition definition, string prefab)
    {
        Source = source;
        Definition = definition;
        Prefab = prefab;
    }

    public DefinitionSource Source { get; }

    public NpcDefinition Definition { get; }

    public string Prefab { get; }
}
}
