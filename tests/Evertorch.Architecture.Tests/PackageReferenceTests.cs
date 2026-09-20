using System.Collections.Generic;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
[TestFixture]
public sealed class PackageReferenceTests
{
    // Every third-party package is an owner-visible decision recorded in THIRD_PARTY_NOTICES.md.
    private static readonly IReadOnlyDictionary<string, string[]> AllowedPackages =
        new Dictionary<string, string[]>
        {
            ["Evertorch.Game"] = new string[0],
            ["Evertorch.Protocol"] = new string[0],
            ["Evertorch.Rules"] = new string[0],
            ["Evertorch.Persistence"] = new string[0],
            ["Evertorch.Server"] = new[] { "Microsoft.Extensions.Hosting" },
            ["Evertorch.Tools"] = new[] { "YamlDotNet" },
        };

    private static IEnumerable<string> KnownProjects => AllowedPackages.Keys;

    [TestCaseSource(nameof(KnownProjects))]
    public void PackageReferences_ForRuntimeProject_MatchAllowedPackages(string projectName)
    {
        Assert.That(
            RepositoryLayout.PackageReferences(projectName),
            Is.EquivalentTo(AllowedPackages[projectName]));
    }

    [Test]
    public void RuntimeProjectNames_InRepository_AreAllCoveredByAllowedPackages()
    {
        Assert.That(RepositoryLayout.RuntimeProjectNames(), Is.EquivalentTo(KnownProjects));
    }
}
}
