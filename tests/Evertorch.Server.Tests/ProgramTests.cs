using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

    // The console output, watched for the host's own word that every service has started.
    private sealed class StartedWatcher : TextWriter
    {
        private readonly StringBuilder m_text = new();

        public ManualResetEventSlim Started { get; } = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            Write(value.ToString());
        }

        public override void Write(string? value)
        {
            lock (m_text)
            {
                m_text.Append(value);
                if (m_text.ToString().Contains("Application started", StringComparison.Ordinal))
                {
                    Started.Set();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Started.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ShutdownOnceStarted : TextReader
    {
        private readonly ManualResetEventSlim m_started;
        private bool m_hasAnswered;

        public ShutdownOnceStarted(ManualResetEventSlim started)
        {
            m_started = started;
        }

        public override string? ReadLine()
        {
            if (m_hasAnswered)
            {
                return null;
            }

            m_hasAnswered = true;
            m_started.Wait(TimeSpan.FromSeconds(30));
            return "shutdown";
        }
    }

    [Test]
    public void Main_WhenItsHealthPortIsTaken_ReturnsOne()
    {
        using var package = new TemporaryDirectory();
        PackageFixture.WriteTo(package.Path, PackageFixture.BuildRepositoryPackage());
        var taken = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        taken.Start();
        try
        {
            int port = ((IPEndPoint)taken.LocalEndpoint).Port;

            int exitCode = RunMain(
                $"--Content:ServerPackagePath={package.Path}",
                $"--ConnectionStrings:Evertorch={TestHosts.UnreachableDatabase}",
                "--Network:Port=0",
                "--Health:Enabled=true",
                $"--Health:Port={port}");

            Assert.That(exitCode, Is.EqualTo(1));
        }
        finally
        {
            taken.Stop();
        }
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
            $"--Network:Port={port}",
            "--Health:Port=0");

        Assert.That(exitCode, Is.EqualTo(1));
    }

    [Test]
    public void Main_WhenTheConsoleSaysShutdown_StopsAndReturnsZero()
    {
        using var package = new TemporaryDirectory();
        PackageFixture.WriteTo(package.Path, PackageFixture.BuildRepositoryPackage());
        TextReader originalIn = Console.In;
        TextWriter originalOut = Console.Out;
        using var output = new StartedWatcher();
        int exitCode;

        // The stop Ctrl+C makes, once the host has started; a stop during the start is a failed start.
        Console.SetOut(output);
        Console.SetIn(new ShutdownOnceStarted(output.Started));
        try
        {
            exitCode = RunMain(
                $"--Content:ServerPackagePath={package.Path}",
                $"--ConnectionStrings:Evertorch={TestHosts.UnreachableDatabase}",
                "--Network:Port=0",
                "--Health:Enabled=true",
                "--Health:Port=0");
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }

        Assert.That(output.Started.IsSet, Is.True, "the host started");
        Assert.That(exitCode, Is.Zero, "a clean stop is not a failure");
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
            "--Network:Port=0",
            "--Health:Port=0");

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
                "--Network:Port=0",
                "--Health:Port=0"
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
                    $"--ConnectionStrings:Evertorch={TestHosts.UnreachableDatabase}",
                    "--Health:Port=0"
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
