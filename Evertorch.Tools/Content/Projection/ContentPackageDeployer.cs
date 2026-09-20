using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace Evertorch.Tools
{
/// <summary>
/// Publishes generated packages to disk. The output directory is replaced as a whole, only when it is
/// recognisably this tool's own output, and never left half-written.
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

        string staging = output + ".staging";
        string previous = output + ".previous";
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
                "Refusing to replace '" + output + "': it is not empty and holds no generated content manifest.");
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
