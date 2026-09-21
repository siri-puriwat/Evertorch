using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
/// Unity's runtime on one side and the real .NET server process on the other, over a simulated bad link. This is
/// the one place that proves both runtimes compute the same movement while messages are delayed and lost.
/// Needs <c>scripts/verify.ps1</c> (or a Release build plus a content build) to have produced the server first.
/// </summary>
public sealed class LiveServerConvergenceTests
{
    private const float StartTimeoutSeconds = 30f;
    private const float StepTimeoutSeconds = 15f;
    private const float ConvergedDistance = 1e-3f;
    private const string ServerDll = "artifacts/bin/Evertorch.Server/release/Evertorch.Server.dll";

    private readonly List<string> m_serverOutput = new List<string>();
    private Process? m_server;
    private LiteNetLibClientTransport? m_socket;

    [TearDown]
    public void StopEverything()
    {
        m_socket?.Dispose();
        m_socket = null;
        if (m_server != null)
        {
            if (!m_server.HasExited)
            {
                m_server.Kill();
                m_server.WaitForExit(5000);
            }

            m_server.Dispose();
            m_server = null;
        }
    }

    [UnityTest]
    public IEnumerator Client_OnABadLink_EndsWhereTheServerProcessSaysItIs()
    {
        string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string serverDll = Path.Combine(repository, ServerDll);
        string serverContent = Path.Combine(repository, "artifacts", "content", "server");
        ClientContent? content = LoadClientContent(out string contentError);
        if (!File.Exists(serverDll) || !Directory.Exists(serverContent) || content == null)
        {
            Assert.Inconclusive(
                "Needs the built server, its content package, and the client package. Run scripts/verify.ps1 and "
                + StreamingContentLoader.MissingPackageHint + " " + contentError);
        }

        int port = FindFreeUdpPort();
        StartServer(serverDll, serverContent, port);
        yield return WaitUntil(() => HasOutput("Listening for clients"), StartTimeoutSeconds);
        Assert.That(HasOutput("Listening for clients"), Is.True, "server output: " + JoinOutput());

        m_socket = new LiteNetLibClientTransport("evertorch", 5000);
        LossyTransport link = new LossyTransport(m_socket, 9, () => Time.realtimeSinceStartupAsDouble)
        {
            LatencyMilliseconds = 60,
            JitterMilliseconds = 15,
            LossPercent = 10,
            ReorderPercent = 5,
        };
        ClientConnection connection = new ClientConnection(
            link,
            new ClientConnectionSettings("0.2.0-dev", content!.Version, "dev:playmode", new CharacterId(31)),
            content);
        connection.Connect("127.0.0.1", port);
        yield return WaitUntil(
            () =>
            {
                connection.Poll();
                return connection.State == ClientConnectionState.InWorld;
            },
            StepTimeoutSeconds);
        Assert.That(
            connection.State,
            Is.EqualTo(ClientConnectionState.InWorld),
            connection.LocalError + " " + connection.DisconnectCause + " server output: " + JoinOutput());

        ClientWorld world = connection.World!;
        MovementController controller = new MovementController(world.Grid);
        LocalPlayerDriver driver = new LocalPlayerDriver(controller, new MoveIntentProducer(), world, connection);
        FixedTickClock clock = new FixedTickClock(1f / connection.ServerTickRate);

        controller.SetManualDirection(1f, 0.5f);
        yield return Simulate(connection, driver, world, clock, 1.5f, () => false);
        controller.SetManualDirection(0f, 0f);
        Func<bool> isAcknowledged = () => world.Predictor.PendingCount == 0;
        yield return Simulate(connection, driver, world, clock, StepTimeoutSeconds, isAcknowledged);
        Assert.That(world.Predictor.PendingCount, Is.EqualTo(0), "every input was acknowledged or forgotten");

        // The server republishes what its console reads once a second, so wait out one full period of quiet.
        yield return Simulate(connection, driver, world, clock, 1.5f, () => false);
        lock (m_serverOutput)
        {
            m_serverOutput.Clear();
        }

        m_server!.StandardInput.WriteLine("players");
        yield return WaitUntil(() => HasOutput(" at ("), StepTimeoutSeconds);

        WorldPosition predicted = world.Predictor.Position;
        Assert.That(TryReadServerPosition(out float serverX, out float serverZ), Is.True, JoinOutput());
        Assert.That(predicted.X, Is.GreaterThan(3f), "the player really walked");
        Assert.That(link.Dropped, Is.GreaterThan(0), "the link really lost messages");
        Assert.That(world.Smoother.Snaps, Is.EqualTo(0));
        Assert.That(predicted.X, Is.EqualTo(serverX).Within(ConvergedDistance), "server output: " + JoinOutput());
        Assert.That(predicted.Z, Is.EqualTo(serverZ).Within(ConvergedDistance));
        UnityEngine.Debug.Log(
            "Live convergence: client " + predicted + ", server (" + serverX + ", " + serverZ + "), largest correction "
            + world.Smoother.LargestCorrection + " m, dropped " + link.Dropped + ", reordered " + link.Reordered);
    }

    private static ClientContent? LoadClientContent(out string error)
    {
        string folder = Path.Combine(Application.streamingAssetsPath, StreamingContentLoader.FolderName);
        string manifestPath = Path.Combine(folder, ClientContentParser.ManifestFile);
        if (!File.Exists(manifestPath))
        {
            error = "No client package in StreamingAssets.";
            return null;
        }

        byte[] manifest = File.ReadAllBytes(manifestPath);
        Dictionary<string, byte[]> files = ClientContentParser
            .ReadFileList(manifest, out error)
            .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(folder, name)));
        return ClientContentParser.Parse(manifest, files, out error);
    }

    private static int FindFreeUdpPort()
    {
        using (UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        }
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator Simulate(
        ClientConnection connection,
        LocalPlayerDriver driver,
        ClientWorld world,
        FixedTickClock clock,
        float seconds,
        Func<bool> isDone)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline && !isDone())
        {
            connection.Poll();
            int due = clock.Advance(Time.unscaledDeltaTime);
            for (int index = 0; index < due; index++)
            {
                driver.Tick(clock.NextTick());
            }

            world.Advance(Time.unscaledDeltaTime);
            yield return null;
        }
    }

    private void StartServer(string serverDll, string serverContent, int port)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "\"" + serverDll + "\" --Network:Port=" + port
                + " --DevelopmentAuthentication:Enabled=true"
                + " --Content:ServerPackagePath=\"" + serverContent + "\"",
            WorkingDirectory = Path.GetDirectoryName(serverDll),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        m_server = new Process { StartInfo = start };
        m_server.OutputDataReceived += (_, line) => Record(line.Data);
        m_server.ErrorDataReceived += (_, line) => Record(line.Data);
        m_server.Start();
        m_server.BeginOutputReadLine();
        m_server.BeginErrorReadLine();
    }

    private void Record(string? line)
    {
        if (line != null)
        {
            lock (m_serverOutput)
            {
                m_serverOutput.Add(line);
            }
        }
    }

    private bool HasOutput(string text)
    {
        lock (m_serverOutput)
        {
            return m_serverOutput.Any(line => line.Contains(text));
        }
    }

    private string JoinOutput()
    {
        lock (m_serverOutput)
        {
            return string.Join(" / ", m_serverOutput);
        }
    }

    private bool TryReadServerPosition(out float x, out float z)
    {
        x = 0f;
        z = 0f;
        Regex position = new Regex(@" at \(([^,]+), ([^,]+), ([^)]+)\)");
        lock (m_serverOutput)
        {
            foreach (string line in m_serverOutput)
            {
                Match match = position.Match(line);
                if (match.Success)
                {
                    x = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    z = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    return true;
                }
            }
        }

        return false;
    }
}
}
