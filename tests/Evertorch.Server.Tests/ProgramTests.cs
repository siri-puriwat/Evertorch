using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
[NonParallelizable]
public sealed class ProgramTests
{
    private static Task<int> StartMain(string[] args)
    {
        return Task.Run(() => Program.Main(args));
    }

    // A server that started after all would run until stopped; the test fails instead of waiting for it.
    private static int RunMain(params string[] args)
    {
        Task<int> run = StartMain(args);
        Assert.That(run.Wait(TimeSpan.FromSeconds(60)), Is.True, "Main returned");
        return run.Result;
    }

    [Test]
    public void Main_WhenItsPortIsTaken_ReturnsOne()
    {
        using var package = new TemporaryDirectory();
        PackageFixture.WriteTo(package.Path, PackageFixture.BuildRepositoryPackage());
        using var taken = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true };
        taken.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)taken.Client.LocalEndPoint!).Port;

        int exitCode = RunMain(
            $"--Content:ServerPackagePath={package.Path}",
            $"--ConnectionStrings:Evertorch={TestHosts.UnreachableDatabase}",
            $"--Network:Port={port}");

        Assert.That(exitCode, Is.EqualTo(1));
    }

    [Test]
    public void Main_WhenTheDatabaseRefusesItsCredentials_ReturnsOne()
    {
        using var database = PostgresFixture.Start();
        using var package = new TemporaryDirectory();
        PackageFixture.WriteTo(package.Path, PackageFixture.BuildRepositoryPackage());
        string refused = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Password = "wrong" }
            .ConnectionString;

        int exitCode = RunMain(
            $"--Content:ServerPackagePath={package.Path}",
            $"--ConnectionStrings:Evertorch={refused}",
            "--Network:Port=0");

        Assert.That(exitCode, Is.EqualTo(1));
    }

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
