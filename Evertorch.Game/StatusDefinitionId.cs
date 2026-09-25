using System;

namespace Evertorch.Game
{
/// <summary>
///     Stable namespaced identity of a status effect, such as <c>status.focus</c>.
///     The default value holds an empty string and names no definition.
/// </summary>
public readonly struct StatusDefinitionId : IEquatable<StatusDefinitionId>
{
    public const string KindPrefix = "status";

    private readonly string? m_value;

    public StatusDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid status effect ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(StatusDefinitionId left, StatusDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StatusDefinitionId left, StatusDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out StatusDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new StatusDefinitionId(value!);
        return true;
    }

    public bool Equals(StatusDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is StatusDefinitionId other && Equals(other);
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
