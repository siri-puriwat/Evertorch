using System;
using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Evertorch.Tools
{
public delegate bool TryCreateId<T>(string? value, out T id);

/// <summary>
///     Reads typed fields from one YAML mapping and reports every problem with its file, field path, and line.
///     Read methods return a default after reporting, so a definition reader can collect all errors in one pass
///     and then discard the definition.
/// </summary>
public sealed class YamlFieldReader
{
    private readonly YamlMappingNode m_node;
    private readonly string m_file;
    private readonly string m_path;
    private readonly List<ContentDiagnostic> m_diagnostics;
    private readonly Dictionary<string, int> m_fieldLines;
    private readonly HashSet<string> m_readKeys = new(StringComparer.Ordinal);
    private readonly List<YamlFieldReader> m_children = new();

    // A reader standing in for a missing mapping: the absence is already reported, so its fields stay quiet.
    private readonly bool m_isPlaceholder;

    private YamlFieldReader(
        YamlMappingNode node,
        string file,
        string path,
        List<ContentDiagnostic> diagnostics,
        Dictionary<string, int> fieldLines,
        bool isPlaceholder)
    {
        m_node = node;
        m_file = file;
        m_path = path;
        m_diagnostics = diagnostics;
        m_fieldLines = fieldLines;
        m_isPlaceholder = isPlaceholder;
    }

    public static YamlFieldReader ForRoot(YamlMappingNode node, string file, List<ContentDiagnostic> diagnostics)
    {
        return new YamlFieldReader(
            node,
            file,
            string.Empty,
            diagnostics,
            new Dictionary<string, int>(StringComparer.Ordinal),
            false);
    }

    public DefinitionSource ToSource()
    {
        return new DefinitionSource(m_file, m_fieldLines);
    }

    public string RequiredString(string key)
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return string.Empty;
        }

        string value = scalar.Value ?? string.Empty;
        if (value.Trim().Length == 0)
        {
            Report(key, scalar, "must not be empty");
            return string.Empty;
        }

        if (value.Trim().Length != value.Length)
        {
            Report(key, scalar, "must not start or end with whitespace");
            return string.Empty;
        }

        return value;
    }

    public string RequiredAssetKey(string key)
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return string.Empty;
        }

        string value = scalar.Value ?? string.Empty;
        if (!IsAssetKey(value))
        {
            Report(
                key,
                scalar,
                "must be a logical asset key of lowercase letters, digits, underscores, and hyphens, not a path");
            return string.Empty;
        }

        return value;
    }

    /// <summary>
    ///     A colour written "#RRGGBB", returned in capitals. The value is quoted, since YAML reads an unquoted # as
    ///     the start of a comment.
    /// </summary>
    public string RequiredColor(string key)
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return string.Empty;
        }

        string value = scalar.Value ?? string.Empty;
        if (!IsColor(value))
        {
            Report(key, scalar, "must be a colour written \"#RRGGBB\", in quotes");
            return string.Empty;
        }

        return value.ToUpperInvariant();
    }

    public T RequiredId<T>(string key, TryCreateId<T> tryCreate, string kindPrefix)
        where T : struct
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return default;
        }

        if (!tryCreate(scalar.Value, out T id))
        {
            Report(
                key,
                scalar,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is not a valid ID; expected '{1}.' followed by lowercase dot-separated segments, {2} characters at most",
                    scalar.Value,
                    kindPrefix,
                    DefinitionIdLimits.MaxLength));
            return default;
        }

        return id;
    }

    public int RequiredInt(string key, int min, int max)
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return 0;
        }

        if (scalar.Style != ScalarStyle.Plain
            || !int.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
        {
            Report(key, scalar, "must be a whole number");
            return 0;
        }

        if (value < min || value > max)
        {
            Report(
                key,
                scalar,
                string.Format(CultureInfo.InvariantCulture, "must be between {0} and {1}", min, max));
            return 0;
        }

        return value;
    }

    public bool RequiredBool(string key)
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return false;
        }

        if (scalar.Style != ScalarStyle.Plain || (scalar.Value != "true" && scalar.Value != "false"))
        {
            Report(key, scalar, "must be true or false");
            return false;
        }

        return scalar.Value == "true";
    }

    public double RequiredDouble(string key, double min, double max, bool isMinExclusive)
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return 0d;
        }

        if (scalar.Style != ScalarStyle.Plain
            || !double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            Report(key, scalar, "must be a finite number");
            return 0d;
        }

        bool isBelowMin = isMinExclusive ? value <= min : value < min;
        if (isBelowMin || value > max)
        {
            Report(
                key,
                scalar,
                string.Format(
                    CultureInfo.InvariantCulture,
                    isMinExclusive ? "must be greater than {0} and at most {1}" : "must be between {0} and {1}",
                    min,
                    max));
            return 0d;
        }

        return value;
    }

    public TEnum RequiredEnum<TEnum>(string key)
        where TEnum : struct, Enum
    {
        YamlScalarNode? scalar = RequiredScalar(key);
        if (scalar == null)
        {
            return default;
        }

        var allowed = new List<string>();
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            string name = EnumText.Of(candidate);
            if (string.Equals(name, scalar.Value, StringComparison.Ordinal))
            {
                return candidate;
            }

            allowed.Add(name);
        }

        Report(
            key,
            scalar,
            string.Format(CultureInfo.InvariantCulture, "must be one of: {0}", string.Join(", ", allowed)));
        return default;
    }

    public YamlFieldReader RequiredMapping(string key)
    {
        YamlNode? node = Find(key);
        if (node is YamlMappingNode mapping)
        {
            return AddChild(mapping, PathOf(key), false);
        }

        if (node == null)
        {
            ReportMissing(key);
        }
        else
        {
            Report(key, node, "must be a mapping");
        }

        return AddChild(new YamlMappingNode(), PathOf(key), true);
    }

    public IReadOnlyList<YamlFieldReader> OptionalMappingSequence(string key)
    {
        var readers = new List<YamlFieldReader>();
        YamlNode? node = Find(key);
        if (node == null)
        {
            return readers;
        }

        if (!(node is YamlSequenceNode sequence))
        {
            Report(key, node, "must be a sequence");
            return readers;
        }

        for (int index = 0; index < sequence.Children.Count; index++)
        {
            string elementPath = string.Format(CultureInfo.InvariantCulture, "{0}[{1}]", PathOf(key), index);
            YamlNode element = sequence.Children[index];
            if (element is YamlMappingNode mapping)
            {
                readers.Add(AddChild(mapping, elementPath, false));
            }
            else
            {
                AddDiagnostic(elementPath, element, "must be a mapping");
            }
        }

        return readers;
    }

    public IReadOnlyList<YamlFieldReader> RequiredMappingSequence(string key)
    {
        if (Find(key) == null)
        {
            ReportMissing(key);
            return new List<YamlFieldReader>();
        }

        return OptionalMappingSequence(key);
    }

    /// <summary>
    ///     Elements are returned as written. Each one's line is kept under <c>key[index]</c> for
    ///     <see cref="ReportField" />. A missing or malformed sequence yields an empty list and one diagnostic.
    /// </summary>
    public IReadOnlyList<string> RequiredStringSequence(string key)
    {
        var values = new List<string>();
        YamlNode? node = Find(key);
        if (node == null)
        {
            ReportMissing(key);
            return values;
        }

        if (!(node is YamlSequenceNode sequence))
        {
            Report(key, node, "must be a sequence");
            return values;
        }

        for (int index = 0; index < sequence.Children.Count; index++)
        {
            string elementPath = string.Format(CultureInfo.InvariantCulture, "{0}[{1}]", PathOf(key), index);
            YamlNode element = sequence.Children[index];
            if (!(element is YamlScalarNode scalar))
            {
                AddDiagnostic(elementPath, element, "must be a single value");
                return new List<string>();
            }

            m_fieldLines[elementPath] = LineOf(element);
            values.Add(scalar.Value ?? string.Empty);
        }

        return values;
    }

    /// <summary>
    ///     Whole numbers from <paramref name="min" /> to <paramref name="max" />, each reported at its own index. A
    ///     missing or malformed sequence yields an empty list and one diagnostic.
    /// </summary>
    public IReadOnlyList<int> RequiredIntSequence(string key, int min, int max)
    {
        var values = new List<int>();
        YamlNode? node = Find(key);
        if (node == null)
        {
            ReportMissing(key);
            return values;
        }

        if (!(node is YamlSequenceNode sequence))
        {
            Report(key, node, "must be a sequence");
            return values;
        }

        for (int index = 0; index < sequence.Children.Count; index++)
        {
            string elementPath = string.Format(CultureInfo.InvariantCulture, "{0}[{1}]", PathOf(key), index);
            YamlNode element = sequence.Children[index];
            m_fieldLines[elementPath] = LineOf(element);
            int value = 0;
            bool isWholeNumber = element is YamlScalarNode scalar
                && scalar.Style == ScalarStyle.Plain
                && int.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
            if (!isWholeNumber)
            {
                AddDiagnostic(elementPath, element, "must be a whole number");
                continue;
            }

            if (value < min || value > max)
            {
                AddDiagnostic(
                    elementPath,
                    element,
                    string.Format(CultureInfo.InvariantCulture, "must be between {0} and {1}", min, max));
                continue;
            }

            values.Add(value);
        }

        return values;
    }

    public bool Has(string key)
    {
        return Find(key) != null;
    }

    /// <summary>Reports every key nothing asked for, here and in every nested mapping that was read.</summary>
    public void ReportUnknownFields()
    {
        if (m_isPlaceholder)
        {
            return;
        }

        foreach (KeyValuePair<YamlNode, YamlNode> child in m_node.Children)
        {
            if (!(child.Key is YamlScalarNode keyNode) || keyNode.Value == null)
            {
                AddDiagnostic(m_path, child.Key, "field names must be plain text");
                continue;
            }

            if (!m_readKeys.Contains(keyNode.Value))
            {
                AddDiagnostic(PathOf(keyNode.Value), keyNode, "unknown field");
            }
        }

        foreach (YamlFieldReader child in m_children)
        {
            child.ReportUnknownFields();
        }
    }

    /// <summary>Reports a problem found by comparing fields that were each valid on their own.</summary>
    public void ReportField(string key, string message)
    {
        if (m_isPlaceholder)
        {
            return;
        }

        string fieldPath = PathOf(key);
        if (!m_fieldLines.TryGetValue(fieldPath, out int line))
        {
            // The field is absent, so point at the mapping that should have held it.
            line = LineOf(m_node);
        }

        m_diagnostics.Add(new ContentDiagnostic(m_file, fieldPath, line, message));
    }

    public string PathOf(string key)
    {
        return m_path.Length == 0 ? key : $"{m_path}.{key}";
    }

    private static bool IsColor(string value)
    {
        if (value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAssetKey(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool isAllowed = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '_'
                || character == '-';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }

    private YamlFieldReader AddChild(YamlMappingNode mapping, string path, bool isPlaceholder)
    {
        var child = new YamlFieldReader(
            mapping,
            m_file,
            path,
            m_diagnostics,
            m_fieldLines,
            m_isPlaceholder || isPlaceholder);
        m_children.Add(child);
        return child;
    }

    private YamlScalarNode? RequiredScalar(string key)
    {
        YamlNode? node = Find(key);
        if (node == null)
        {
            ReportMissing(key);
            return null;
        }

        if (!(node is YamlScalarNode scalar))
        {
            Report(key, node, "must be a single value");
            return null;
        }

        return scalar;
    }

    private YamlNode? Find(string key)
    {
        m_readKeys.Add(key);
        foreach (KeyValuePair<YamlNode, YamlNode> child in m_node.Children)
        {
            if (child.Key is YamlScalarNode keyNode && string.Equals(keyNode.Value, key, StringComparison.Ordinal))
            {
                m_fieldLines[PathOf(key)] = LineOf(child.Value);
                return child.Value;
            }
        }

        return null;
    }

    private void ReportMissing(string key)
    {
        if (!m_isPlaceholder)
        {
            AddDiagnostic(PathOf(key), m_node, "required field is missing");
        }
    }

    private void Report(string key, YamlNode node, string message)
    {
        AddDiagnostic(PathOf(key), node, message);
    }

    private void AddDiagnostic(string fieldPath, YamlNode node, string message)
    {
        m_diagnostics.Add(new ContentDiagnostic(m_file, fieldPath, LineOf(node), message));
    }

    private static int LineOf(YamlNode node)
    {
        return (int)node.Start.Line;
    }
}
}
