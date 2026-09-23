using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
/// <summary>
///     EF Core and Npgsql are dependencies of every project that references <c>Evertorch.Persistence</c>, so the
///     assembly checks cannot keep them out of the server. This reads the sources instead (Persistence §2).
/// </summary>
[TestFixture]
public sealed class PersistenceSourceBoundaryTests
{
    private const string PersistenceProject = "Evertorch.Persistence";

    // The namespaces in a using directive or a qualified name.
    private static readonly Regex DatabaseNamespace = new(
        @"\b(Microsoft\.EntityFrameworkCore|Npgsql)\s*[;.]",
        RegexOptions.CultureInvariant);

    // The server asks for pending migrations but never applies one (Persistence §2).
    private static readonly Regex MigrationApply = new(@"\bApplyMigrationsAsync\b", RegexOptions.CultureInvariant);

    private static IEnumerable<string> OtherRuntimeProjects()
    {
        return RepositoryLayout.RuntimeProjectNames().Where(name => name != PersistenceProject).OrderBy(name => name);
    }

    private static IEnumerable<string> SourcesOf(string projectName)
    {
        string root = Path.Combine(RepositoryLayout.RootDirectory, projectName);
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories);
    }

    private static List<string> Offenders(IEnumerable<string> paths, Regex pattern)
    {
        return paths
            .Where(path => pattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryLayout.RootDirectory, path))
            .ToList();
    }

    [TestCaseSource(nameof(OtherRuntimeProjects))]
    public void Sources_OutsidePersistence_DoNotUseEfCoreOrNpgsql(string projectName)
    {
        Assert.That(Offenders(SourcesOf(projectName), DatabaseNamespace), Is.Empty);
    }

    [TestCase("using Microsoft.EntityFrameworkCore;", true)]
    [TestCase("using Npgsql;", true)]
    [TestCase("var x = new Npgsql.NpgsqlConnection();", true)]
    [TestCase("// Npgsql is only named here", false)]
    [TestCase("IGameStore store;", false)]
    public void DatabaseNamespacePattern_MatchesOnlyNamespaceUse(string line, bool isMatch)
    {
        Assert.That(DatabaseNamespace.IsMatch(line), Is.EqualTo(isMatch));
    }

    [Test]
    public void ClientSources_DoNotUseEfCoreOrNpgsql()
    {
        IEnumerable<string> sources = Directory.EnumerateFiles(
            RepositoryLayout.ClientFilePath("Assets"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.That(Offenders(sources, DatabaseNamespace), Is.Empty);
    }

    [Test]
    public void PersistenceSources_UseEfCore_SoTheGuardAboveIsNotVacuous()
    {
        Assert.That(Offenders(SourcesOf(PersistenceProject), DatabaseNamespace), Is.Not.Empty);
    }

    [Test]
    public void ServerSources_NeverApplyMigrations()
    {
        Assert.That(Offenders(SourcesOf("Evertorch.Server"), MigrationApply), Is.Empty);
    }
}
}
