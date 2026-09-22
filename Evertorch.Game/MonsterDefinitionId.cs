using System;

namespace Evertorch.Game
{
/// <summary>
///     Stable namespaced identity of a monster definition, such as <c>monster.training_slime</c>.
///     The default value holds an empty string and names no definition.
/// </summary>
public readonly struct MonsterDefinitionId : IEquatable<MonsterDefinitionId>
{
    public const string KindPrefix = "monster";

    private readonly string? m_value;

    public MonsterDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid monster definition ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(MonsterDefinitionId left, MonsterDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MonsterDefinitionId left, MonsterDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out MonsterDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new MonsterDefinitionId(value!);
        return true;
    }

    public bool Equals(MonsterDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is MonsterDefinitionId other && Equals(other);
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
