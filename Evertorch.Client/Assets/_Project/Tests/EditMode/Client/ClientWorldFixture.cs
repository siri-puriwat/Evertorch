using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client.Tests.EditMode
{
internal static class ClientWorldFixture
{
    public const uint TickRate = 20;

    public static readonly EntityId LocalEntity = new EntityId(100);

    public static ClientWorld Create(NavigationGrid grid, WorldPosition position, uint serverTick = 0)
    {
        return new ClientWorld(grid, Entered(position, serverTick), TickRate);
    }

    public static WorldEntered Entered(WorldPosition position, uint serverTick = 0)
    {
        return new WorldEntered(
            new MapDefinitionId("map.training_ground"),
            1,
            LocalEntity,
            serverTick,
            position,
            new WorldDirection(0f, 1f),
            ClientTestGrids.Speed);
    }

    public static EntitySnapshot Snapshot(uint tick, uint acknowledged, params EntityState[] states)
    {
        return new EntitySnapshot(tick, acknowledged, states);
    }

    public static EntityState State(EntityId entity, WorldPosition position)
    {
        return new EntityState(entity, position, new WorldDirection(0f, 1f), 0f, 0f, 0f, EntityStateFlags.None);
    }
}
}
