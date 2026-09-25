using System;

namespace Evertorch.Game
{
/// <summary>
///     A way to another map (Gameplay Systems §4.2): a circle on the floor, and where a character that stands inside it
///     arrives. Portals are server-only content.
/// </summary>
public sealed class MapPortal
{
    public MapPortal(
        WorldPosition center,
        double radius,
        MapDefinitionId destinationMap,
        WorldPosition destinationPosition,
        WorldDirection destinationFacing)
    {
        if (!(radius > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "A portal needs a positive radius.");
        }

        Center = center;
        Radius = radius;
        DestinationMap = destinationMap;
        DestinationPosition = destinationPosition;
        DestinationFacing = destinationFacing;
    }

    public WorldPosition Center { get; }

    /// <summary>World units.</summary>
    public double Radius { get; }

    public MapDefinitionId DestinationMap { get; }

    public WorldPosition DestinationPosition { get; }

    public WorldDirection DestinationFacing { get; }

    /// <summary>
    ///     Whether a character whose center is at <paramref name="position" /> stands inside, measured on the ground.
    /// </summary>
    public bool Contains(WorldPosition position)
    {
        double dx = position.X - Center.X;
        double dz = position.Z - Center.Z;
        return dx * dx + dz * dz <= Radius * Radius;
    }
}
}
