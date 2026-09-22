using System;
using System.IO;

namespace Evertorch.Server.Tests
{
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "evertorch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
            // A virus scanner or file watcher may still hold a handle; a leftover temp directory is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Write(string relativePath, string content)
    {
        string path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
}
