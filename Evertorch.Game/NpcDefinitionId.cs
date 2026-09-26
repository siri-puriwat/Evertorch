using System;

namespace Evertorch.Game
{
/// <summary>
///     Stable namespaced identity of an NPC, such as <c>npc.quartermaster</c>.
///     The default value holds an empty string and names no definition.
/// </summary>
public readonly struct NpcDefinitionId : IEquatable<NpcDefinitionId>
{
    public const string KindPrefix = "npc";

    private readonly string? m_value;

    public NpcDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid NPC ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(NpcDefinitionId left, NpcDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(NpcDefinitionId left, NpcDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out NpcDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new NpcDefinitionId(value!);
        return true;
    }

    public bool Equals(NpcDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is NpcDefinitionId other && Equals(other);
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
