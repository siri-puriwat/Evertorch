using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
/// <summary>
///     Credential rules no test can observe at run time, read from the sources (Network Protocol §4): how long a
///     comparison of a password's key takes must not depend on how much of it matched, and the client keeps the login
///     and the password in memory only.
/// </summary>
[TestFixture]
public sealed class CredentialSourceTests
{
    // Every way the client's runtime code could keep something past its process.
    private static readonly Regex Persisting = new(
        @"\bPlayerPrefs\.|\bFile\.(Write|Append|Create|Open)|\bFileStream\b|\bStreamWriter\b",
        RegexOptions.CultureInvariant);

    [Test]
    public void ClientRuntimeSources_NeverWritePlayerPrefsOrFiles()
    {
        IEnumerable<string> sources = Directory.EnumerateFiles(
            RepositoryLayout.ClientFilePath("Assets/_ProjectScripts.Evertorch.Client"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.That(sources.Where(path => Persisting.IsMatch(File.ReadAllText(path))).Select(Path.GetFileName),
            Is.Empty);
    }

    [Test]
    public void PasswordHasher_ComparesKeysInFixedTimeOnly()
    {
        string source = File.ReadAllText(
            Path.Combine(RepositoryLayout.RootDirectory, "Evertorch.Server", "Accounts", "PasswordHasher.cs"));

        Assert.That(source, Does.Contain("CryptographicOperations.FixedTimeEquals("));
        Assert.That(source, Does.Not.Contain("SequenceEqual").And.Not.Contain(".Equals(actual"));
    }
}
}
