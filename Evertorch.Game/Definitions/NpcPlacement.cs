namespace Evertorch.Game
{
/// <summary>
///     Where a map places an NPC (Content Pipeline §4): inside one of the map's NPC marker cells, facing
///     <see cref="Facing" />.
/// </summary>
public sealed class NpcPlacement
{
    public NpcPlacement(NpcDefinitionId npc, WorldPosition position, WorldDirection facing)
    {
        Npc = npc;
        Position = position;
        Facing = facing;
    }

    public NpcDefinitionId Npc { get; }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }
}
}
