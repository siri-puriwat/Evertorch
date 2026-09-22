using System.Globalization;

namespace Evertorch.Tools
{
/// <summary>
///     One content error, located by source file, field path, and line so an author can fix it without searching.
/// </summary>
public sealed class ContentDiagnostic
{
    public ContentDiagnostic(string file, string fieldPath, int line, string message)
    {
        File = file;
        FieldPath = fieldPath;
        Line = line;
        Message = message;
    }

    /// <summary>Path relative to the content root, with forward slashes.</summary>
    public string File { get; }

    /// <summary>Dotted path such as <c>drops[0].amount.min</c>; empty for a whole-file problem.</summary>
    public string FieldPath { get; }

    /// <summary>One-based line, or zero when the problem has no single line.</summary>
    public int Line { get; }

    public string Message { get; }

    public override string ToString()
    {
        string location = Line > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}({1})", File, Line)
            : File;

        return FieldPath.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}: {1}: {2}", location, FieldPath, Message)
            : string.Format(CultureInfo.InvariantCulture, "{0}: {1}", location, Message);
    }
}
}
