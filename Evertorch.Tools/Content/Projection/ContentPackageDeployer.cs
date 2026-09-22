using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Evertorch.Tools
{
/// <summary>
///     Publishes generated packages to disk. The output directory is replaced as a whole, only when it is
///     recognisably this tool's own output, and never left half-written.
/// </summary>
public static class ContentPackageDeployer
{
    public const string ServerDirectory = "server";
    public const string ClientDirectory = "client";

    private const int MoveAttempts = 20;
    private const int MoveRetryDelayMs = 100;

    public static void Deploy(ContentPackages packages, string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        EnsureReplaceable(output);

        string staging = $"{output}.staging";
        string previous = $"{output}.previous";
        DeleteIfPresent(staging);
        DeleteIfPresent(previous);

        try
        {
            WritePackage(packages.Server, Path.Combine(staging, ServerDirectory));
            WritePackage(packages.Client, Path.Combine(staging, ClientDirectory));
        }
        catch
        {
            DeleteIfPresent(staging);
            throw;
        }

        if (Directory.Exists(output))
        {
            MoveWithRetry(output, previous);
        }

        MoveWithRetry(staging, output);
        DeleteIfPresent(previous);
    }

    /// <summary>
    ///     Copies the client package into a directory another tool also writes to, such as a Unity
    ///     <c>StreamingAssets</c> folder. Files are replaced in place so that tool's own side files survive; the
    ///     manifest is removed first and written last, so an interrupted copy reads as incomplete, never as valid.
    /// </summary>
    public static void DeployClient(ContentPackage client, string clientDirectory)
    {
        string directory = Path.GetFullPath(clientDirectory);
        string manifest = Path.Combine(directory, ContentPackage.ManifestPath);
        if (Directory.Exists(directory) && !IsOwnOutput(client, directory, manifest))
        {
            throw new InvalidOperationException(
                $"Refusing to write into '{directory}': it holds JSON files but no generated content manifest.");
        }

        Directory.CreateDirectory(directory);
        if (File.Exists(manifest))
        {
            File.Delete(manifest);
        }

        foreach (string existing in Directory.GetFiles(directory, "*.json"))
        {
            if (client.DataFiles.All(file => file.Path != Path.GetFileName(existing)))
            {
                File.Delete(existing);
            }
        }

        WritePackage(client, directory);
    }

    // With a manifest, the manifest decides. Without one the folder is either empty of JSON or an interrupted copy,
    // which holds nothing but this package's own data files and can simply be completed.
    private static bool IsOwnOutput(ContentPackage client, string directory, string manifest)
    {
        if (File.Exists(manifest))
        {
            return IsGeneratedClientManifest(manifest);
        }

        return Directory
            .EnumerateFiles(directory, "*.json")
            .All(existing => client.DataFiles.Any(file => file.Path == Path.GetFileName(existing)));
    }

    // A file that merely has the right name is not proof: Unity's own Packages folder holds a manifest.json too,
    // and treating that folder as this tool's output would delete its other JSON files.
    private static bool IsGeneratedClientManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("schemaVersion", out JsonElement _)
                && root.TryGetProperty("clientContentVersion", out JsonElement _)
                && root.TryGetProperty("files", out JsonElement files)
                && files.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void EnsureReplaceable(string output)
    {
        if (!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any())
        {
            return;
        }

        string marker = Path.Combine(output, ServerDirectory, ContentPackage.ManifestPath);
        if (!File.Exists(marker))
        {
            throw new InvalidOperationException(
                $"Refusing to replace '{output}': it is not empty and holds no generated content manifest.");
        }
    }

    private static void WritePackage(ContentPackage package, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (PackageFile file in package.DataFiles)
        {
            File.WriteAllBytes(Path.Combine(directory, file.Path), file.Content);
        }

        // The manifest goes last: a directory without one is by definition incomplete.
        File.WriteAllBytes(Path.Combine(directory, package.Manifest.Path), package.Manifest.Content);
    }

    // On Windows a virus scanner or indexer can briefly hold files that were just written, which makes the
    // directory rename fail with access denied. The rename is retried rather than replaced by a file copy
    // so the swap stays a single step.
    private static void MoveWithRetry(string source, string destination)
    {
        for (int attempt = 1;; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException) when (attempt < MoveAttempts)
            {
                Thread.Sleep(MoveRetryDelayMs);
            }
            catch (UnauthorizedAccessException) when (attempt < MoveAttempts)
            {
                Thread.Sleep(MoveRetryDelayMs);
            }
        }
    }

    private static void DeleteIfPresent(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
}
