using System;

namespace Evertorch.Game
{
/// <summary>
/// Stable namespaced identity of a skill definition, such as <c>skill.novice.first_aid</c>.
/// The default value holds an empty string and names no definition.
/// </summary>
public readonly struct SkillDefinitionId : IEquatable<SkillDefinitionId>
{
    public const string KindPrefix = "skill";

    private readonly string? m_value;

    public SkillDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid skill definition ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(SkillDefinitionId left, SkillDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SkillDefinitionId left, SkillDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out SkillDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new SkillDefinitionId(value!);
        return true;
    }

    public bool Equals(SkillDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is SkillDefinitionId other && Equals(other);
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
