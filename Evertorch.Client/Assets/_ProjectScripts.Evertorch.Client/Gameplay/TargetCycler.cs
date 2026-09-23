using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Chooses the target to request when the player cycles: candidates nearest first (then by entity ID), stepping
///     forward or back from the current target and wrapping around. Only entities the client already knows are
///     candidates, so cycling reveals nothing hidden.
/// </summary>
public sealed class TargetCycler
{
    private readonly List<PickCandidate> m_ordered = new();

    public EntityId Choose(
        IReadOnlyList<PickCandidate> candidates,
        WorldPosition from,
        EntityId current,
        bool isForward)
    {
        m_ordered.Clear();
        m_ordered.AddRange(candidates);
        if (m_ordered.Count == 0)
        {
            return default;
        }

        m_ordered.Sort((left, right) =>
        {
            int byDistance = DistanceSquared(left.Position, from).CompareTo(DistanceSquared(right.Position, from));
            return byDistance != 0 ? byDistance : left.Entity.Value.CompareTo(right.Entity.Value);
        });

        int index = m_ordered.FindIndex(candidate => candidate.Entity == current);
        if (index < 0)
        {
            return isForward ? m_ordered[0].Entity : m_ordered[m_ordered.Count - 1].Entity;
        }

        int step = isForward ? 1 : -1;
        return m_ordered[(index + step + m_ordered.Count) % m_ordered.Count].Entity;
    }

    private static float DistanceSquared(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
}
}
