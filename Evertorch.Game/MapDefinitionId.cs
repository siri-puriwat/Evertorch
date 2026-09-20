using System;

namespace Evertorch.Game
{
/// <summary>
/// Stable namespaced identity of a map definition, such as <c>map.training_ground</c>.
/// The default value holds an empty string and names no definition.
/// </summary>
public readonly struct MapDefinitionId : IEquatable<MapDefinitionId>
{
    public const string KindPrefix = "map";

    private readonly string? m_value;

    public MapDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid map definition ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(MapDefinitionId left, MapDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MapDefinitionId left, MapDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out MapDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new MapDefinitionId(value!);
        return true;
    }

    public bool Equals(MapDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is MapDefinitionId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }
}
}
