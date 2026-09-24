using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A throwaway PostgreSQL 18 container for the live server, migrated with <c>scripts/db-migrate.ps1</c> and
///     removed on dispose. It never touches the Compose development database. Needs Docker running.
/// </summary>
internal sealed class LiveDatabase : IDisposable
{
    private const string Image = "postgres:18";
    private const string Database = "evertorch_live";
    private const int CommandTimeoutMs = 180_000;
    private const int ReadyTimeoutMs = 60_000;

    private readonly string m_docker;
    private string? m_container;

    private LiveDatabase(string docker)
    {
        m_docker = docker;
    }

    public string ConnectionString { get; private set; } = string.Empty;

    private static string Repository => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    public void Dispose()
    {
        if (m_container == null)
        {
            return;
        }

        TryRun(m_docker, $"rm -f {m_container}", out _);
        m_container = null;
    }

    /// <summary>
    ///     Starts and migrates the database off the main thread; the test yields until the task completes.
    /// </summary>
    public static Task<LiveDatabase> StartAsync()
    {
        return Task.Run(Start);
    }

    private static LiveDatabase Start()
    {
        var database = new LiveDatabase(DockerCommand());
        try
        {
            database.Create();
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    private static string DockerCommand()
    {
        string installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Docker",
            "Docker",
            "resources",
            "bin",
            "docker.exe");
        return File.Exists(installed) ? installed : "docker";
    }

    private static string Run(string fileName, string arguments)
    {
        if (!TryRun(fileName, arguments, out string output))
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(fileName)} {arguments.Split(' ')[0]}' failed. The live tests need Docker running. "
                + output);
        }

        return output;
    }

    private static bool TryRun(string fileName, string arguments, out string output)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Repository,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        try
        {
            using var process = Process.Start(start)!;
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(CommandTimeoutMs))
            {
                process.Kill();
                output = "timed out";
                return false;
            }

            // Docker reports progress, such as pulling a missing image, on stderr; a successful command's output is
            // stdout alone, or a container ID would carry the pull log with it.
            bool isSuccess = process.ExitCode == 0;
            output = isSuccess ? standardOutput.Result : standardOutput.Result + standardError.Result;
            return isSuccess;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          || exception is Win32Exception)
        {
            output = exception.Message;
            return false;
        }
    }

    private void Create()
    {
        // The password lives only as long as this container, which is bound to loopback on a port Docker picks.
        string password = Guid.NewGuid().ToString("N");
        m_container = Run(
                m_docker,
                $"run -d --rm -e POSTGRES_PASSWORD={password} -e POSTGRES_DB={Database} -p 127.0.0.1::5432 {Image}")
            .Trim();
        string mapping = Run(m_docker, $"port {m_container} 5432/tcp")
            .Split('\n')
            .Select(line => line.Trim())
            .First(line => line.Length > 0);
        string port = mapping.Substring(mapping.LastIndexOf(':') + 1);

        // During its first start the image runs an initialization server on the socket only; TCP answers once the
        // real server is up.
        var ready = Stopwatch.StartNew();
        while (!TryRun(m_docker, $"exec {m_container} pg_isready -h 127.0.0.1 -U postgres -d {Database}", out _))
        {
            if (ready.ElapsedMilliseconds > ReadyTimeoutMs)
            {
                throw new InvalidOperationException("The live test database did not become ready.");
            }

            Thread.Sleep(250);
        }

        ConnectionString =
            $"Host=127.0.0.1;Port={port};Database={Database};Username=postgres;Password={password}";
        string script = Path.Combine(Repository, "scripts", "db-migrate.ps1");
        Run("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -ConnectionString \"{ConnectionString}\"");
    }
}
}
