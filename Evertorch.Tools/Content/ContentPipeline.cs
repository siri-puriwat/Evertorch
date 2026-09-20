using System.Collections.Generic;

namespace Evertorch.Tools
{
public static class ContentPipeline
{
    /// <summary>Loads and validates a content root. Packages are built only when there are no diagnostics.</summary>
    public static ContentPipelineResult Run(string contentRoot)
    {
        List<ContentDiagnostic> diagnostics = new List<ContentDiagnostic>();
        ContentSet content = ContentLoader.Load(contentRoot, diagnostics);
        ContentValidator.Validate(content, diagnostics);

        ContentPackages? packages = diagnostics.Count == 0 ? ContentPackageBuilder.Build(content) : null;
        return new ContentPipelineResult(content, diagnostics, packages);
    }
}
}
