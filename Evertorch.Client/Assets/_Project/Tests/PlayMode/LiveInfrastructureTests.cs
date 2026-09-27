using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     What keeps the live tests from leaving a server process or a database container behind when the editor dies in
///     the middle of one, and what manual play on this machine needs (Coding Standards §10).
/// </summary>
public sealed class LiveInfrastructureTests
{
    private const int DockerTestTimeoutMs = 120_000;

    private Process? m_process;
    private LiveDatabase? m_database;

    [TearDown]
    public void StopEverything()
    {
        if (m_process != null)
        {
            if (!m_process.HasExited)
            {
                m_process.Kill();
                m_process.WaitForExit(5000);
            }

            m_process.Dispose();
            m_process = null;
        }

        m_database?.Dispose();
        m_database = null;
    }

    [Test]
    public void AProcessInTheJob_EndsWhenTheJobsLastHandleCloses()
    {
        if (!KillOnCloseJob.IsSupported)
        {
            Assert.Inconclusive("Job objects are a Windows feature.");
        }

        // Long enough to outlive the test by far unless something ends it.
        m_process = Process.Start(
            new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
        var job = KillOnCloseJob.Create();
        job.Add(m_process);
        bool isInJob = job.Contains(m_process);

        // What Windows does to every handle when the editor exits.
        job.Dispose();
        bool hasEnded = m_process.WaitForExit(5000);

        Assert.That(isInJob, Is.True);
        Assert.That(hasEnded, Is.True, "closing the job's last handle ended the process");
    }

    // The live tests pin the gateway's certificate and need only that it exists; a player's client signing in from
    // the editor, a player build, or a browser needs the machine to trust it (Network Protocol §4).
    [Test]
    public void TheDevelopmentCertificate_IsTrusted()
    {
        m_process = Process.Start(
            new ProcessStartInfo("dotnet", "dev-certs https --check --trust")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            })!;
        string output = m_process.StandardOutput.ReadToEnd();
        bool hasEnded = m_process.WaitForExit(30_000);

        Assert.That(hasEnded, Is.True, "dotnet dev-certs answered");
        Assert.That(m_process.ExitCode, Is.Zero, $"run `dotnet dev-certs https --trust` once: {output}");
    }

    [UnityTest]
    [Timeout(DockerTestTimeoutMs)]
    public IEnumerator StartAsync_RemovesTheLabelledContainersAnEarlierRunLeftBehind_WithTheirVolumes()
    {
        // A container an editor left behind; creating it makes the image's anonymous data volume too.
        string leftover = LiveDatabase.Docker($"create --label {LiveDatabase.Label} postgres:18").Trim();
        string volume = LiveDatabase.Docker("inspect -f \"{{range .Mounts}}{{.Name}}{{end}}\" " + leftover).Trim();

        Task<LiveDatabase> starting = LiveDatabase.StartAsync();
        yield return new WaitUntil(() => starting.IsCompleted);
        Assert.That(starting.IsFaulted, Is.False, starting.Exception?.GetBaseException().Message);
        m_database = starting.Result;
        string[] labelled = Lines(LiveDatabase.Docker($"ps -aq --no-trunc --filter label={LiveDatabase.Label}"));
        string[] volumes = Lines(LiveDatabase.Docker($"volume ls -q --filter name={volume}"));

        Assert.That(volume, Is.Not.Empty, "the leftover had a volume to lose");
        Assert.That(labelled, Does.Not.Contain(leftover));
        Assert.That(labelled.Length, Is.EqualTo(1), "only the new database carries the label");
        Assert.That(volumes, Is.Empty, "the leftover's volume went with it");
    }

    private static string[] Lines(string output)
    {
        return output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
}
