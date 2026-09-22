using System.Collections.Generic;

namespace Evertorch.Tools
{
/// <summary>
///     Where a definition was authored, kept so cross-definition checks can still report a file, field, and line.
/// </summary>
public sealed class DefinitionSource
{
    private readonly IReadOnlyDictionary<string, int> m_fieldLines;

    public DefinitionSource(string file, IReadOnlyDictionary<string, int> fieldLines)
    {
        File = file;
        m_fieldLines = fieldLines;
    }

    public string File { get; }

    public int LineOf(string fieldPath)
    {
        return m_fieldLines.TryGetValue(fieldPath, out int line) ? line : 0;
    }
}
}
