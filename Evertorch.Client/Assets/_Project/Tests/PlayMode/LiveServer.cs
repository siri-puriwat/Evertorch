using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The real .NET server as a child process, for tests that run Unity's runtime against it. Needs
///     <c>scripts/verify.ps1</c> (or a Release build plus a content build) to have produced the server first. On
///     Windows the process is in a <see cref="KillOnCloseJob" />, so it ends with the editor.
/// </summary>
internal sealed class LiveServer : IDisposable
{
    /// <summary>
    ///     The password of every account a test makes (<see cref="CreateAccount" />).
    /// </summary>
    public const string AccountPassword = "Live-Password-1";

    private const string ServerDll = "artifacts/bin/Evertorch.Server/release/Evertorch.Server.dll";
    private const float AccountTimeoutSeconds = 15f;

    private static readonly Regex GatewayListening =
        new(@"Gateway listening on https://[^:]+:(\d+) with the certificate ([0-9A-F]+)\.");

    private static readonly Regex AccountCreated = new(@"^Account \d+ created\.$");

    private readonly List<string> m_output = new();
    private Process? m_process;
    private KillOnCloseJob? m_job;
    private int m_accounts;

    // Kept apart from the output, which a test may clear before it reads a command's answer.
    private int m_createdAccounts;
    private int m_gatewayPort;
    private string m_thumbprint = string.Empty;

    public static string MissingPrerequisites =>
        "Needs the built server, its content package, and the client package. "
        + $"Run scripts/verify.ps1 and {StreamingContentLoader.MissingPackageHint}";

    private static string Repository => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    private static string DllPath => Path.Combine(Repository, ServerDll);

    /// <summary>
    ///     The server package the tools built from the repository's content, which a test may copy and edit.
    /// </summary>
    public static string ContentPath => Path.Combine(Repository, "artifacts", "content", "server");

    private static string ClientContentFolder =>
        Path.Combine(Application.streamingAssetsPath, StreamingContentLoader.FolderName);

    /// <summary>
    ///     Whether the process is in the job that ends it with the editor.
    /// </summary>
    public bool IsTiedToEditor => m_process != null && m_job != null && m_job.Contains(m_process);

    public void Dispose()
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

        m_job?.Dispose();
        m_job = null;
    }

    public static bool IsBuilt()
    {
        return File.Exists(DllPath) && Directory.Exists(ContentPath) && HasClientPackage();
    }

    /// <summary>
    ///     Null when the client package in StreamingAssets is the one the server package was built with; otherwise
    ///     what differs and how to fix it. The handshake would refuse a stale package as a content update, which says
    ///     nothing about the cause.
    /// </summary>
    public static string? ContentMismatch()
    {
        string expected = ClientVersionIn(Path.Combine(ContentPath, ClientContentParser.ManifestFile));
        string actual = ClientVersionIn(Path.Combine(ClientContentFolder, ClientContentParser.ManifestFile));
        return string.Equals(expected, actual, StringComparison.Ordinal)
            ? null
            : $"The client content in StreamingAssets is version {actual}, but the server package expects {expected}. "
            + StreamingContentLoader.MissingPackageHint;
    }

    public static ClientContent? LoadClientContent(out string error)
    {
        string folder = ClientContentFolder;
        byte[] manifest = File.ReadAllBytes(Path.Combine(folder, ClientContentParser.ManifestFile));
        var files = ClientContentParser
            .ReadFileList(manifest, out error)
            .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(folder, name)));
        return ClientContentParser.Parse(manifest, files, out error);
    }

    private static bool HasClientPackage()
    {
        return File.Exists(Path.Combine(ClientContentFolder, ClientContentParser.ManifestFile));
    }

    private static string ClientVersionIn(string manifestPath)
    {
        return JsonUtility.FromJson<ManifestVersion>(File.ReadAllText(manifestPath)).clientContentVersion;
    }

    /// <summary>
    ///     Starts the server on a port it picks itself, so nothing can take one between a probe and the bind, with
    ///     <paramref name="database" /> as its database, and the repository's server package unless
    ///     <paramref name="contentPath" /> names another.
    /// </summary>
    public void Start(LiveDatabase database, string extraArguments = "", string? contentPath = null)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            // The gateway serves the current user's development certificate, which the tests pin by its logged
            // thumbprint rather than trust. Development sign-in stays on for the bare connections of some tests.
            Arguments = $"\"{DllPath}\" --Network:Port=0 --Health:Port=0 --Gateway:Port=0"
                + " --Accounts:PasswordIterations=1000 --DevelopmentAuthentication:Enabled=true"
                + $" --Content:ServerPackagePath=\"{contentPath ?? ContentPath}\" {extraArguments}",
            WorkingDirectory = Path.GetDirectoryName(DllPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // The environment rather than an argument, as the launcher does.
        start.EnvironmentVariables["ConnectionStrings__Evertorch"] = database.ConnectionString;
        m_process = new Process { StartInfo = start };
        m_process.OutputDataReceived += (_, line) => Record(line.Data);
        m_process.ErrorDataReceived += (_, line) => Record(line.Data);
        m_process.Start();
        if (KillOnCloseJob.IsSupported)
        {
            m_job = KillOnCloseJob.Create();
            m_job.Add(m_process);
        }

        m_process.BeginOutputReadLine();
        m_process.BeginErrorReadLine();
    }

    public void SendCommand(string command)
    {
        m_process?.StandardInput.WriteLine(command);
    }

    public void ClearOutput()
    {
        lock (m_output)
        {
            m_output.Clear();
        }
    }

    public string[] Output()
    {
        lock (m_output)
        {
            return m_output.ToArray();
        }
    }

    public bool HasOutput(string text)
    {
        return Output().Any(line => line.Contains(text));
    }

    public string JoinOutput()
    {
        return string.Join(" / ", Output());
    }

    /// <summary>
    ///     The position of <paramref name="character" /> in what the console's <c>players</c> command printed.
    /// </summary>
    public bool TryReadPlayerPosition(long character, out float x, out float z)
    {
        x = 0f;
        z = 0f;
        var position = new Regex(@" at \(([^,]+), ([^,]+), ([^)]+)\)");
        string named = CharacterMarker(character);
        foreach (string line in Output())
        {
            Match match = position.Match(line);
            if (match.Success && line.Contains(named))
            {
                x = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                z = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The text that names <paramref name="character" /> on its line of the <c>players</c> output, for waiting on
    ///     that line rather than on the first line of any player.
    /// </summary>
    public static string CharacterMarker(long character)
    {
        return $"character {character.ToString(CultureInfo.InvariantCulture)} ";
    }

    /// <summary>
    ///     Makes an account at the server's console, as the operator does (Network Protocol §4), and waits for its
    ///     answer. Each call makes the next of <c>live1</c>, <c>live2</c>, and so on, with <see cref="AccountPassword" />.
    /// </summary>
    public string CreateAccount()
    {
        m_accounts++;
        string login = $"live{m_accounts.ToString(CultureInfo.InvariantCulture)}";
        SendCommand($"account create {login} {AccountPassword}");
        var waited = Stopwatch.StartNew();
        while (Volatile.Read(ref m_createdAccounts) < m_accounts)
        {
            if (waited.Elapsed.TotalSeconds > AccountTimeoutSeconds)
            {
                throw new TimeoutException($"The account {login} was not made: {JoinOutput()}");
            }

            Thread.Sleep(20);
        }

        return login;
    }

    /// <summary>
    ///     The gateway's port and its certificate's thumbprint, from its listening line.
    /// </summary>
    public bool TryReadGateway(out int port, out string thumbprint)
    {
        lock (m_output)
        {
            port = m_gatewayPort;
            thumbprint = m_thumbprint;
            return port != 0;
        }
    }

    public bool TryReadListeningPort(out int port)
    {
        port = 0;
        var listening = new Regex(@"Listening for clients on [^:]+:(\d+)");
        foreach (string line in Output())
        {
            Match match = listening.Match(line);
            if (match.Success)
            {
                port = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }

    private void Record(string? line)
    {
        if (line != null)
        {
            lock (m_output)
            {
                m_output.Add(line);
                Match gateway = GatewayListening.Match(line);
                if (gateway.Success)
                {
                    m_gatewayPort = int.Parse(gateway.Groups[1].Value, CultureInfo.InvariantCulture);
                    m_thumbprint = gateway.Groups[2].Value;
                }
            }

            if (AccountCreated.IsMatch(line))
            {
                Interlocked.Increment(ref m_createdAccounts);
            }
        }
    }

    // Both packages' manifests carry the client version. JsonUtility binds by field name, so the field keeps the
    // manifest's JSON spelling, and it ignores the rest.
    // ReSharper disable InconsistentNaming
    [Serializable]
    private sealed class ManifestVersion
    {
        public string clientContentVersion = string.Empty;
    }
    // ReSharper restore InconsistentNaming
}
}
