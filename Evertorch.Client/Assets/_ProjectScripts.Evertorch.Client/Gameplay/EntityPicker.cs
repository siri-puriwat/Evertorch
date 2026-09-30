using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Finds the entity under a pointer ray. Entity views carry no colliders, so that ground clicks reach the map;
///     each candidate is instead a sphere around its body. Of the spheres the ray passes through, the one whose centre
///     lies nearest the ray wins, then the one the ray enters first, then the lower entity ID, so a large body never
///     takes a click aimed at a small one beside it (Prototype Content §4).
/// </summary>
public static class EntityPicker
{
    public const float PickRadius = 0.7f;
    public const float PickHeight = 0.5f;

    // Centres this close to the ray count as equally near, so rounding never lets a farther body on the same line win.
    private const double SameMiss = 0.0001;

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
        double nearestMiss = double.MaxValue;
        double nearestHit = double.MaxValue;
        foreach (PickCandidate candidate in candidates)
        {
            double radius = candidate.Radius;
            double cx = candidate.Position.X - origin.X;
            double cy = candidate.Position.Y + candidate.CenterHeight - origin.Y;
            double cz = candidate.Position.Z - origin.Z;
            double along = cx * dx + cy * dy + cz * dz;
            double closestSquared = Math.Max(0.0, cx * cx + cy * cy + cz * cz - along * along);
            if (along <= 0.0 || closestSquared > radius * radius)
            {
                continue;
            }

            double miss = Math.Sqrt(closestSquared);
            double hit = along - Math.Sqrt(radius * radius - closestSquared);
            bool isSameMiss = Math.Abs(miss - nearestMiss) <= SameMiss;
            bool isBetter = (!isSameMiss && miss < nearestMiss)
                || (isSameMiss && (hit < nearestHit || (hit == nearestHit && candidate.Entity.Value < entity.Value)));
            if (isBetter)
            {
                nearestMiss = miss;
                nearestHit = hit;
                entity = candidate.Entity;
            }
        }

        return entity != default;
    }
}
}
