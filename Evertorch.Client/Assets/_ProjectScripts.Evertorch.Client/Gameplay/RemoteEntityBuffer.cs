using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Recent server states of one remote entity, drawn a little in the past so there are usually two states to blend
///     between. Remote entities are never predicted: past the newest state the entity simply waits.
/// </summary>
public sealed class RemoteEntityBuffer
{
    public const double InterpolationDelaySeconds = 0.1;
    public const int Capacity = 32;

    private readonly List<Sample> m_samples = new();

    public int Count => m_samples.Count;

    public void Add(double serverTimeSeconds, WorldPosition position, WorldDirection facing)
    {
        if (m_samples.Count > 0 && serverTimeSeconds <= m_samples[m_samples.Count - 1].Time)
        {
            return;
        }

        if (m_samples.Count == Capacity)
        {
            m_samples.RemoveAt(0);
        }

        m_samples.Add(new Sample(serverTimeSeconds, position, facing));
    }

    public void Clear()
    {
        m_samples.Clear();
    }

    /// <param name="renderTimeSeconds">Server time to draw, already reduced by the interpolation delay.</param>
    public bool TrySample(double renderTimeSeconds, out WorldPosition position, out WorldDirection facing)
    {
        position = default;
        facing = default;
        if (m_samples.Count == 0)
        {
            return false;
        }

        // Everything before the state that starts the current blend is no longer needed.
        while (m_samples.Count >= 2 && m_samples[1].Time <= renderTimeSeconds)
        {
            m_samples.RemoveAt(0);
        }

        Sample from = m_samples[0];
        if (m_samples.Count == 1 || renderTimeSeconds <= from.Time)
        {
            position = from.Position;
            facing = from.Facing;
            return true;
        }

        Sample to = m_samples[1];
        if (IsTeleport(from.Position, to.Position))
        {
            position = to.Position;
            facing = to.Facing;
            return true;
        }

        float t = (float)((renderTimeSeconds - from.Time) / (to.Time - from.Time));
        position = new WorldPosition(
            from.Position.X + (to.Position.X - from.Position.X) * t,
            from.Position.Y + (to.Position.Y - from.Position.Y) * t,
            from.Position.Z + (to.Position.Z - from.Position.Z) * t);
        WorldDirection blended = MovementModel.NormalizeOrZero(
            from.Facing.X + (to.Facing.X - from.Facing.X) * t,
            from.Facing.Z + (to.Facing.Z - from.Facing.Z) * t);
        facing = blended == default ? to.Facing : blended;
        return true;
    }

    private static bool IsTeleport(WorldPosition from, WorldPosition to)
    {
        float deltaX = to.X - from.X;
        float deltaY = to.Y - from.Y;
        float deltaZ = to.Z - from.Z;
        float threshold = RenderSmoother.TeleportThreshold;
        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ > threshold * threshold;
    }

    private readonly struct Sample
    {
        public Sample(double time, WorldPosition position, WorldDirection facing)
        {
            Time = time;
            Position = position;
            Facing = facing;
        }

        public double Time { get; }

        public WorldPosition Position { get; }

        public WorldDirection Facing { get; }
    }
}
}
