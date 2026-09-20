using System.Collections.Generic;

namespace Evertorch.Tools
{
public sealed class ContentPipelineResult
{
    public ContentPipelineResult(
        ContentSet content,
        IReadOnlyList<ContentDiagnostic> diagnostics,
        ContentPackages? packages)
    {
        Content = content;
        Diagnostics = diagnostics;
        Packages = packages;
    }

    public ContentSet Content { get; }

    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }

    /// <summary>Null whenever <see cref="Diagnostics" /> is not empty.</summary>
    public ContentPackages? Packages { get; }
}
}
