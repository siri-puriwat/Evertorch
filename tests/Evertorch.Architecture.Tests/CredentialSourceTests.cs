using System.IO;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
/// <summary>
///     How long a comparison of a password's key takes must not depend on how much of it matched (Network Protocol
///     §4). No test can time that reliably, so this reads the source.
/// </summary>
[TestFixture]
public sealed class CredentialSourceTests
{
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
