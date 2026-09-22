using System;

namespace Evertorch.Game
{
/// <summary>
///     One square of a <see cref="NavigationGrid" />: what stands there and how high its ground is.
/// </summary>
public readonly struct NavigationCell : IEquatable<NavigationCell>
{
    public NavigationCell(NavigationSurface surface, RampAxis axis, float heightAtMin, float heightAtMax)
    {
        if (!IsFinite(heightAtMin) || !IsFinite(heightAtMax))
        {
            throw new ArgumentException("Cell heights must be finite.");
        }

        if (axis == RampAxis.None && !heightAtMin.Equals(heightAtMax))
        {
            throw new ArgumentException("A level cell must have one height.");
        }

        Surface = surface;
        Axis = axis;
        HeightAtMin = heightAtMin;
        HeightAtMax = heightAtMax;
    }

    public NavigationSurface Surface { get; }

    public RampAxis Axis { get; }

    /// <summary>
    ///     Ground height at the cell edge with the smaller coordinate on <see cref="Axis" />.
    /// </summary>
    public float HeightAtMin { get; }

    /// <summary>
    ///     Ground height at the cell edge with the larger coordinate on <see cref="Axis" />.
    /// </summary>
    public float HeightAtMax { get; }

    public bool IsWalkable => Surface == NavigationSurface.Floor;

    public static bool operator ==(NavigationCell left, NavigationCell right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(NavigationCell left, NavigationCell right)
    {
        return !left.Equals(right);
    }

    public static NavigationCell Level(NavigationSurface surface, float height)
    {
        return new NavigationCell(surface, RampAxis.None, height, height);
    }

    public static NavigationCell Ramp(RampAxis axis, float heightAtMin, float heightAtMax)
    {
        if (axis == RampAxis.None)
        {
            throw new ArgumentException("A ramp needs an axis.", nameof(axis));
        }

        return new NavigationCell(NavigationSurface.Floor, axis, heightAtMin, heightAtMax);
    }

    public bool Equals(NavigationCell other)
    {
        return Surface == other.Surface
            && Axis == other.Axis
            && HeightAtMin.Equals(other.HeightAtMin)
            && HeightAtMax.Equals(other.HeightAtMax);
    }

    public override bool Equals(object? obj)
    {
        return obj is NavigationCell other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Surface, Axis, HeightAtMin, HeightAtMax);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
}
