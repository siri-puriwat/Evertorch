using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
/// The real .NET server as a child process, for tests that run Unity's runtime against it. Needs
/// <c>scripts/verify.ps1</c> (or a Release build plus a content build) to have produced the server first.
/// </summary>
internal sealed class LiveServer : IDisposable
{
    private const string ServerDll = "artifacts/bin/Evertorch.Server/release/Evertorch.Server.dll";

    private readonly List<string> m_output = new List<string>();
    private Process? m_process;

    public static string MissingPrerequisites =>
        "Needs the built server, its content package, and the client package. "
        + $"Run scripts/verify.ps1 and {StreamingContentLoader.MissingPackageHint}";

    private static string Repository => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    private static string DllPath => Path.Combine(Repository, ServerDll);

    private static string ContentPath => Path.Combine(Repository, "artifacts", "content", "server");

    public static bool IsBuilt()
    {
        return File.Exists(DllPath) && Directory.Exists(ContentPath) && HasClientPackage();
    }

    public static ClientContent? LoadClientContent(out string error)
    {
        string folder = Path.Combine(Application.streamingAssetsPath, StreamingContentLoader.FolderName);
        byte[] manifest = File.ReadAllBytes(Path.Combine(folder, ClientContentParser.ManifestFile));
        Dictionary<string, byte[]> files = ClientContentParser
            .ReadFileList(manifest, out error)
            .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(folder, name)));
        return ClientContentParser.Parse(manifest, files, out error);
    }

    private static bool HasClientPackage()
    {
        return File.Exists(
            Path.Combine(
                Application.streamingAssetsPath,
                StreamingContentLoader.FolderName,
                ClientContentParser.ManifestFile));
    }

    /// <summary>
    /// Starts the server on a port it picks itself, so nothing can take one between a probe and the bind.
    /// </summary>
    public void Start(string extraArguments = "")
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{DllPath}\" --Network:Port=0 --DevelopmentAuthentication:Enabled=true"
                + $" --Content:ServerPackagePath=\"{ContentPath}\" {extraArguments}",
            WorkingDirectory = Path.GetDirectoryName(DllPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        m_process = new Process { StartInfo = start };
        m_process.OutputDataReceived += (_, line) => Record(line.Data);
        m_process.ErrorDataReceived += (_, line) => Record(line.Data);
        m_process.Start();
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

    public bool TryReadListeningPort(out int port)
    {
        port = 0;
        Regex listening = new Regex(@"Listening for clients on [^:]+:(\d+)");
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

    public void Dispose()
    {
        if (m_process == null)
        {
            return;
        }

        if (!m_process.HasExited)
        {
            m_process.Kill();
            m_process.WaitForExit(5000);
        }

        m_process.Dispose();
        m_process = null;
    }

    private void Record(string? line)
    {
        if (line != null)
        {
            lock (m_output)
            {
                m_output.Add(line);
            }
        }
    }
}
}
