using System;

namespace Evertorch.Game
{
/// <summary>
///     Stable namespaced identity of an experience table, such as <c>experience.adventurer</c>.
///     The default value holds an empty string and names no definition.
/// </summary>
public readonly struct ExperienceDefinitionId : IEquatable<ExperienceDefinitionId>
{
    public const string KindPrefix = "experience";

    private readonly string? m_value;

    public ExperienceDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid experience table ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(ExperienceDefinitionId left, ExperienceDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ExperienceDefinitionId left, ExperienceDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out ExperienceDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new ExperienceDefinitionId(value!);
        return true;
    }

    public bool Equals(ExperienceDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ExperienceDefinitionId other && Equals(other);
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
