using System;

namespace Evertorch.Game
{
/// <summary>
///     Stable namespaced identity of a quest, such as <c>quest.crawler_hunt</c>.
///     The default value holds an empty string and names no definition.
/// </summary>
public readonly struct QuestDefinitionId : IEquatable<QuestDefinitionId>
{
    public const string KindPrefix = "quest";

    private readonly string? m_value;

    public QuestDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid quest ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(QuestDefinitionId left, QuestDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(QuestDefinitionId left, QuestDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out QuestDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new QuestDefinitionId(value!);
        return true;
    }

    public bool Equals(QuestDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is QuestDefinitionId other && Equals(other);
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
