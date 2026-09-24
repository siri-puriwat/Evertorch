using System.Collections.Generic;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
/// <summary>
///     Shared frameworks are dependencies the package checks cannot see. Only the server may use ASP.NET Core, for its
///     health endpoints (System Architecture §4, §10).
/// </summary>
[TestFixture]
public sealed class FrameworkReferenceTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedFrameworks =
        new Dictionary<string, string[]>
        {
            ["Evertorch.Game"] = new string[0],
            ["Evertorch.Protocol"] = new string[0],
            ["Evertorch.Rules"] = new string[0],
            ["Evertorch.Persistence"] = new string[0],
            ["Evertorch.Server"] = new[] { "Microsoft.AspNetCore.App" },
            ["Evertorch.Tools"] = new string[0]
        };

    private static IEnumerable<string> KnownProjects => AllowedFrameworks.Keys;

    [TestCaseSource(nameof(KnownProjects))]
    public void FrameworkReferences_ForRuntimeProject_MatchAllowedFrameworks(string projectName)
    {
        Assert.That(
            RepositoryLayout.FrameworkReferences(projectName),
            Is.EquivalentTo(AllowedFrameworks[projectName]));
    }

    [Test]
    public void RuntimeProjectNames_InRepository_AreAllCoveredByAllowedFrameworks()
    {
        Assert.That(RepositoryLayout.RuntimeProjectNames(), Is.EquivalentTo(KnownProjects));
    }
}
}
