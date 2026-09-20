namespace Evertorch.Tools
{
public sealed class PackageFile
{
    public PackageFile(string path, byte[] content)
    {
        Path = path;
        Content = content;
    }

    /// <summary>Path relative to the package directory, with forward slashes.</summary>
    public string Path { get; }

    public byte[] Content { get; }
}
}
