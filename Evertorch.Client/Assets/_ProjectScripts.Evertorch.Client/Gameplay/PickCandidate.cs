using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     An entity a click could select, at the position it is drawn at.
/// </summary>
public readonly struct PickCandidate
{
    public PickCandidate(EntityId entity, WorldPosition position)
    {
        Entity = entity;
        Position = position;
    }

    public EntityId Entity { get; }

    public WorldPosition Position { get; }
}
}
