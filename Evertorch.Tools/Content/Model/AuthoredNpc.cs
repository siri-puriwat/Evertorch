using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class AuthoredNpc
{
    public AuthoredNpc(DefinitionSource source, NpcDefinition definition, string prefab, string? tint = null)
    {
        Source = source;
        Definition = definition;
        Prefab = prefab;
        Tint = tint;
    }

    public DefinitionSource Source { get; }

    public NpcDefinition Definition { get; }

    public string Prefab { get; }

    /// <summary>
    ///     The colour of the NPC's whole body, "#RRGGBB"; null when it keeps its model's own.
    /// </summary>
    public string? Tint { get; }
}
}
