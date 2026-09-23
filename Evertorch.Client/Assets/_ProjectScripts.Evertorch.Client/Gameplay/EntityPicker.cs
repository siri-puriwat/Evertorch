using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Finds the entity under a pointer ray. Entity views carry no colliders, so that ground clicks reach the map;
///     each candidate is instead a sphere around its body, and the nearest along the ray wins (Prototype Content §4).
/// </summary>
public static class EntityPicker
{
    public const float PickRadius = 0.7f;
    public const float PickHeight = 0.5f;

    /// <summary>
    ///     Picks along the ray from <paramref name="origin" />; the direction need not be normalized but must not be
    ///     zero.
    /// </summary>
    public static bool TryPick(
        WorldPosition origin,
        float directionX,
        float directionY,
        float directionZ,
        IReadOnlyList<PickCandidate> candidates,
        out EntityId entity)
    {
        entity = default;
        double length = Math.Sqrt(directionX * directionX + directionY * directionY + directionZ * directionZ);
        if (!(length > 0.0) || double.IsInfinity(length))
        {
            return false;
        }

        double dx = directionX / length;
        double dy = directionY / length;
        double dz = directionZ / length;
        double nearest = double.MaxValue;
        foreach (PickCandidate candidate in candidates)
        {
            double cx = candidate.Position.X - origin.X;
            double cy = candidate.Position.Y + PickHeight - origin.Y;
            double cz = candidate.Position.Z - origin.Z;
            double along = cx * dx + cy * dy + cz * dz;
            double closestSquared = cx * cx + cy * cy + cz * cz - along * along;
            if (along <= 0.0 || closestSquared > PickRadius * PickRadius)
            {
                continue;
            }

            double hit = along - Math.Sqrt(PickRadius * PickRadius - closestSquared);
            if (hit < nearest || (hit == nearest && candidate.Entity.Value < entity.Value))
            {
                nearest = hit;
                entity = candidate.Entity;
            }
        }

        return entity != default;
    }
}
}
