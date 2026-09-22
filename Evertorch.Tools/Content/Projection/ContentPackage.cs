using System.Collections.Generic;

namespace Evertorch.Tools
{
/// <summary>
///     One generated package held in memory: its data files, its content version, and the manifest describing them.
/// </summary>
public sealed class ContentPackage
{
    public const string ManifestPath = "manifest.json";

    public ContentPackage(string version, IReadOnlyList<PackageFile> dataFiles, PackageFile manifest)
    {
        Version = version;
        DataFiles = dataFiles;
        Manifest = manifest;
    }

    public string Version { get; }

    public IReadOnlyList<PackageFile> DataFiles { get; }

    public PackageFile Manifest { get; }
}
}
