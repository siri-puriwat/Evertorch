using System;
using System.IO;
using System.Threading;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
[NonParallelizable]
public sealed class ProgramTests
{
    [Test]
    public void Main_WithPendingMigrations_ReturnsOneWithoutApplyingThem()
    {
        using var database = PostgresFixture.Start(false);
        using var package = new TemporaryDirectory();
        PackageFixture.WriteTo(package.Path, PackageFixture.BuildRepositoryPackage());

        int exitCode = Program.Main(
            new[]
            {
                $"--Content:ServerPackagePath={package.Path}",
                $"--ConnectionStrings:Evertorch={database.ConnectionString}",
                "--Network:Port=0"
            });

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(
            EvertorchDatabase
                .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            Is.Not.Empty,
            "the server must never apply a migration");
    }

    [Test]
    public void Main_WithoutAContentPackage_ReturnsOneAndSaysHowToStartForDevelopment()
    {
        using var root = new TemporaryDirectory();
        string missing = Path.Combine(root.Path, "no-such-package");
        TextWriter original = Console.Error;
        var error = new StringWriter();
        int exitCode;

        Console.SetError(error);
        try
        {
            exitCode = Program.Main(
                new[]
                {
                    $"--Content:ServerPackagePath={missing}",
                    $"--ConnectionStrings:Evertorch={TestHosts.UnreachableDatabase}"
                });
        }
        finally
        {
            Console.SetError(original);
        }

        string text = error.ToString();
        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(text, Does.Contain("no usable content package"));
        Assert.That(text, Does.Contain("scripts\\run-server.cmd"));
        Assert.That(text, Does.Contain("DOTNET_ENVIRONMENT=Development"));
        Assert.That(text, Does.Not.Contain("Press Enter"), "only a window of its own makes the server wait");
    }
}
}
