using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Evertorch.Tools;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Server packages produced by the real content pipeline, plus helpers to damage them in controlled ways.
/// </summary>
internal static class PackageFixture
{
    private const string SolutionFileName = "Evertorch.sln";

    public static string RepositoryContentDirectory => Path.Combine(FindRepositoryRoot(), "content");

    /// <summary>
    ///     Canonical content with one definition of each kind, so a text replacement in a package file changes exactly
    ///     one definition however much the repository's content grows.
    /// </summary>
    public static string FixtureContentDirectory =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "content");

    public static Dictionary<string, byte[]> BuildRepositoryPackage()
    {
        return Build(RepositoryContentDirectory);
    }

    public static Dictionary<string, byte[]> BuildFixturePackage()
    {
        return Build(FixtureContentDirectory);
    }

    public static string ReadText(IReadOnlyDictionary<string, byte[]> files, string file)
    {
        return Encoding.UTF8.GetString(files[file]);
    }

    /// <summary>
    ///     Replaces text in one file and leaves the manifest alone, as tampering or corruption would.
    /// </summary>
    public static void ReplaceWithoutManifest(
        Dictionary<string, byte[]> files,
        string file,
        string oldText,
        string newText)
    {
        string text = ReadText(files, file);
        if (!text.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{oldText}' was not found in {file}.");
        }

        files[file] = Encoding.UTF8.GetBytes(text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Replaces text in a data file and re-signs the manifest, so only the content defect remains to be found.
    /// </summary>
    public static void Replace(Dictionary<string, byte[]> files, string file, string oldText, string newText)
    {
        ReplaceWithoutManifest(files, file, oldText, newText);
        RewriteManifest(files);
    }

    public static void RewriteManifest(Dictionary<string, byte[]> files)
    {
        string[] dataFiles = files.Keys
            .Where(name => name != ServerContentLoader.ManifestFile)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var listing = new StringBuilder();
        var entries = new List<string>();
        foreach (string name in dataFiles)
        {
            string hash = ContentPackageBuilder.ComputeHash(files[name]);
            listing.Append(name).Append(':').Append(hash).Append('\n');
            entries.Add($"{{\"path\":\"{name}\",\"sha256\":\"{hash}\"}}");
        }

        byte[] listingBytes = Encoding.UTF8.GetBytes(listing.ToString());
        string serverVersion = ContentPackageBuilder.ComputeHash(listingBytes).Substring(0, 16);

        var manifest = new StringBuilder();
        manifest.Append("{\"schemaVersion\":1,");
        manifest.Append("\"serverContentVersion\":\"").Append(serverVersion).Append("\",");
        manifest.Append("\"clientContentVersion\":\"0123456789abcdef\",");
        manifest.Append("\"files\":[").Append(string.Join(",", entries)).Append("]}");
        files[ServerContentLoader.ManifestFile] = Encoding.UTF8.GetBytes(manifest.ToString());
    }

    public static void WriteTo(string directory, IReadOnlyDictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(directory);
        foreach (KeyValuePair<string, byte[]> file in files)
        {
            File.WriteAllBytes(Path.Combine(directory, file.Key), file.Value);
        }
    }

    private static Dictionary<string, byte[]> Build(string contentDirectory)
    {
        ContentPipelineResult result = ContentPipeline.Run(contentDirectory);
        if (result.Packages == null)
        {
            string diagnostics = string.Join("; ", result.Diagnostics.Select(item => item.ToString()));
            throw new InvalidOperationException($"{contentDirectory} is invalid: {diagnostics}");
        }

        ContentPackage server = result.Packages.Server;
        var files = server.DataFiles.ToDictionary(
            file => file.Path,
            file => file.Content,
            StringComparer.Ordinal);
        files[server.Manifest.Path] = server.Manifest.Content;
        return files;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{SolutionFileName} was not found above the test directory.");
    }
}
}
