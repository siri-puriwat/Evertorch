using System;

namespace Evertorch.Game
{
/// <summary>
/// Stable namespaced identity of a job definition, such as <c>job.adventurer</c>.
/// The default value holds an empty string and names no definition.
/// </summary>
public readonly struct JobDefinitionId : IEquatable<JobDefinitionId>
{
    public const string KindPrefix = "job";

    private readonly string? m_value;

    public JobDefinitionId(string value)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            throw new ArgumentException("Value is not a valid job definition ID.", nameof(value));
        }

        m_value = value;
    }

    public string Value => m_value ?? string.Empty;

    public static bool operator ==(JobDefinitionId left, JobDefinitionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(JobDefinitionId left, JobDefinitionId right)
    {
        return !left.Equals(right);
    }

    public static bool TryCreate(string? value, out JobDefinitionId id)
    {
        if (!DefinitionIdFormat.IsValid(value, KindPrefix))
        {
            id = default;
            return false;
        }

        id = new JobDefinitionId(value!);
        return true;
    }

    public bool Equals(JobDefinitionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is JobDefinitionId other && Equals(other);
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
