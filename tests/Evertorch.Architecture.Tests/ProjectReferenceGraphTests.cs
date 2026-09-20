using System.Collections.Generic;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
[TestFixture]
public sealed class ProjectReferenceGraphTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
        new Dictionary<string, string[]>
        {
            ["Evertorch.Game"] = new string[0],
            ["Evertorch.Protocol"] = new[] { "Evertorch.Game" },
            ["Evertorch.Rules"] = new[] { "Evertorch.Game" },
            ["Evertorch.Persistence"] = new[] { "Evertorch.Game" },
            ["Evertorch.Server"] = new[]
            {
                "Evertorch.Game",
                "Evertorch.Protocol",
                "Evertorch.Rules",
                "Evertorch.Persistence",
            },
            ["Evertorch.Tools"] = new[] { "Evertorch.Game" },
        };

    private static IEnumerable<string> KnownProjects => AllowedReferences.Keys;

    [TestCaseSource(nameof(KnownProjects))]
    public void ProjectReferences_ForRuntimeProject_MatchAllowedGraph(string projectName)
    {
        Assert.That(
            RepositoryLayout.ProjectReferences(projectName),
            Is.EquivalentTo(AllowedReferences[projectName]));
    }

    [Test]
    public void RuntimeProjectNames_InRepository_AreAllCoveredByAllowedGraph()
    {
        Assert.That(RepositoryLayout.RuntimeProjectNames(), Is.EquivalentTo(KnownProjects));
    }
}
}
