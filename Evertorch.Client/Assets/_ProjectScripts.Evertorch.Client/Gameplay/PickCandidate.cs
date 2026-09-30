using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     An entity a click could select, at the position it is drawn at, with the sphere its body is picked by: its
///     centre's height over that position and its radius (Prototype Content §4).
/// </summary>
public readonly struct PickCandidate
{
    public PickCandidate(
        EntityId entity,
        WorldPosition position,
        float centerHeight = EntityPicker.PickHeight,
        float radius = EntityPicker.PickRadius)
    {
        Entity = entity;
        Position = position;
        CenterHeight = centerHeight;
        Radius = radius;
    }

    public EntityId Entity { get; }

    public WorldPosition Position { get; }

    public float CenterHeight { get; }

    public float Radius { get; }
}
}
