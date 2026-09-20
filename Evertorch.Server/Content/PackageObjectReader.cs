using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Evertorch.Server
{
internal delegate bool TryCreatePackageId<T>(string? value, out T id);

/// <summary>
/// Reads one JSON object of a content package strictly: every property must be asked for, have the expected kind,
/// and appear once. Defects are collected rather than thrown so a bad package is reported in full.
/// </summary>
internal sealed class PackageObjectReader
{
    private readonly JsonElement m_element;
    private readonly string m_file;
    private readonly string m_path;
    private readonly List<string> m_problems;
    private readonly HashSet<string> m_requested = new HashSet<string>(StringComparer.Ordinal);

    private PackageObjectReader(JsonElement element, string file, string path, List<string> problems)
    {
        m_element = element;
        m_file = file;
        m_path = path;
        m_problems = problems;
    }

    public static PackageObjectReader? ForRoot(byte[] content, string file, List<string> problems)
    {
        JsonElement root;
        try
        {
            using (JsonDocument document = JsonDocument.Parse(content))
            {
                root = document.RootElement.Clone();
            }
        }
        catch (JsonException exception)
        {
            problems.Add(file + ": invalid JSON: " + exception.Message);
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            problems.Add(file + ": the root must be an object");
            return null;
        }

        return new PackageObjectReader(root, file, string.Empty, problems);
    }

    public string RequiredString(string name)
    {
        if (!TryGet(name, JsonValueKind.String, "a string", out JsonElement value))
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    public int RequiredInt(string name, int minimum)
    {
        if (!TryGet(name, JsonValueKind.Number, "a whole number", out JsonElement value))
        {
            return minimum;
        }

        if (!value.TryGetInt32(out int number))
        {
            Report(name, "must be a whole number");
            return minimum;
        }

        if (number < minimum)
        {
            Report(name, "must be at least " + minimum);
            return minimum;
        }

        return number;
    }

    public double RequiredDouble(string name)
    {
        if (!TryGet(name, JsonValueKind.Number, "a number", out JsonElement value))
        {
            return 0d;
        }

        if (!value.TryGetDouble(out double number) || double.IsNaN(number) || double.IsInfinity(number))
        {
            Report(name, "must be a finite number");
            return 0d;
        }

        return number;
    }

    public TEnum RequiredEnum<TEnum>(string name)
        where TEnum : struct, Enum
    {
        string text = RequiredString(name);
        foreach (TEnum candidate in (TEnum[])Enum.GetValues(typeof(TEnum)))
        {
            string candidateName = candidate.ToString();
            string spelling = char.ToLowerInvariant(candidateName[0]) + candidateName.Substring(1);
            if (string.Equals(spelling, text, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        if (text.Length != 0)
        {
            Report(name, "unknown value '" + text + "'");
        }

        return default;
    }

    public T RequiredId<T>(string name, TryCreatePackageId<T> tryCreate)
        where T : struct
    {
        string text = RequiredString(name);
        if (tryCreate(text, out T id))
        {
            return id;
        }

        if (text.Length != 0)
        {
            Report(name, "'" + text + "' is not a valid ID of this kind");
        }

        return default;
    }

    public PackageObjectReader? RequiredObject(string name)
    {
        if (!TryGet(name, JsonValueKind.Object, "an object", out JsonElement value))
        {
            return null;
        }

        return new PackageObjectReader(value, m_file, Combine(name), m_problems);
    }

    public IReadOnlyList<PackageObjectReader> RequiredObjectArray(string name)
    {
        List<PackageObjectReader> readers = new List<PackageObjectReader>();
        if (!TryGet(name, JsonValueKind.Array, "an array", out JsonElement value))
        {
            return readers;
        }

        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            string itemPath = Combine(name) + "[" + index + "]";
            if (item.ValueKind == JsonValueKind.Object)
            {
                readers.Add(new PackageObjectReader(item, m_file, itemPath, m_problems));
            }
            else
            {
                m_problems.Add(m_file + ": " + itemPath + ": must be an object");
            }

            index++;
        }

        return readers;
    }

    public IReadOnlyList<string> RequiredStringArray(string name)
    {
        List<string> values = new List<string>();
        if (!TryGet(name, JsonValueKind.Array, "an array", out JsonElement value))
        {
            return values;
        }

        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                m_problems.Add(m_file + ": " + Combine(name) + "[" + index + "]: must be a string");
                return new List<string>();
            }

            values.Add(item.GetString() ?? string.Empty);
            index++;
        }

        return values;
    }

    /// <summary>
    /// Call after every expected property has been read.
    /// </summary>
    public void ReportUnexpectedProperties()
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in m_element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                Report(property.Name, "appears more than once");
            }
            else if (!m_requested.Contains(property.Name))
            {
                Report(property.Name, "unknown property");
            }
        }
    }

    public void Report(string name, string message)
    {
        m_problems.Add(m_file + ": " + Combine(name) + ": " + message);
    }

    private bool TryGet(string name, JsonValueKind kind, string description, out JsonElement value)
    {
        m_requested.Add(name);
        if (!m_element.TryGetProperty(name, out value))
        {
            Report(name, "required property is missing");
            return false;
        }

        if (value.ValueKind != kind)
        {
            Report(name, "must be " + description);
            return false;
        }

        return true;
    }

    private string Combine(string name)
    {
        return m_path.Length == 0 ? name : m_path + "." + name;
    }
}
}
