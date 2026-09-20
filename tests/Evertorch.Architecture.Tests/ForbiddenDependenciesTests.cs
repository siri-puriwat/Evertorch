using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
[TestFixture]
public sealed class ForbiddenDependenciesTests
{
    [TestCase("UnityEngine")]
    [TestCase("UnityEngine.CoreModule")]
    [TestCase("UnityEditor")]
    [TestCase("Unity.Addressables")]
    [TestCase("LiteNetLib")]
    [TestCase("Microsoft.EntityFrameworkCore")]
    [TestCase("Microsoft.EntityFrameworkCore.Relational")]
    [TestCase("Npgsql")]
    [TestCase("Npgsql.EntityFrameworkCore.PostgreSQL")]
    [TestCase("Microsoft.AspNetCore.Http")]
    [TestCase("Microsoft.Extensions.Hosting")]
    [TestCase("Evertorch.Server")]
    [TestCase("Evertorch.Persistence")]
    [TestCase("Evertorch.Rules")]
    [TestCase("Evertorch.Tools")]
    [TestCase("Evertorch.Client")]
    [TestCase("Evertorch.Client.Editor")]
    public void IsForbiddenForSharedAssembly_WhenDependencyCrossesBoundary_ReturnsTrue(string dependencyName)
    {
        Assert.That(ForbiddenDependencies.IsForbiddenForSharedAssembly(dependencyName), Is.True);
    }

    [TestCase("netstandard")]
    [TestCase("System.Runtime")]
    [TestCase("System.Memory")]
    [TestCase("Evertorch.Game")]
    [TestCase("Evertorch.Protocol")]
    public void IsForbiddenForSharedAssembly_WhenDependencyIsAllowed_ReturnsFalse(string dependencyName)
    {
        Assert.That(ForbiddenDependencies.IsForbiddenForSharedAssembly(dependencyName), Is.False);
    }
}
}
