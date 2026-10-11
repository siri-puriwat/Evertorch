using System;

namespace Evertorch.Game
{
/// <summary>
///     Stable namespaced identity of a refine table, such as <c>refine.armor</c>.
///     The default value holds an empty string and names no definition.
/// </summary>
public readonly struct RefineDefinitionId : IEquatable<RefineDefinitionId>
{
    public const string KindPrefix = "refine";

    private readonly string? m_value;

    public RefineDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid refine table ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(RefineDefinitionId left, RefineDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(RefineDefinitionId left, RefineDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out RefineDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new RefineDefinitionId(value!);
        return true;
    }

    public bool Equals(RefineDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is RefineDefinitionId other && Equals(other);
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
