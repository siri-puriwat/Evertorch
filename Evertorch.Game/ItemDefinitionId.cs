using System;

namespace Evertorch.Game
{
/// <summary>
/// Stable namespaced identity of an item definition, such as <c>item.material.slime_gel</c>.
/// The default value holds an empty string and names no definition.
/// </summary>
public readonly struct ItemDefinitionId : IEquatable<ItemDefinitionId>
{
    public const string KindPrefix = "item";

    private readonly string? m_value;

    public ItemDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid item definition ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(ItemDefinitionId left, ItemDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ItemDefinitionId left, ItemDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out ItemDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new ItemDefinitionId(value!);
        return true;
    }

    public bool Equals(ItemDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ItemDefinitionId other && Equals(other);
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
