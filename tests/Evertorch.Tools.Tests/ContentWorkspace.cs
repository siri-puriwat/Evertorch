using System;
using System.IO;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     A throwaway copy of the valid fixture content that one test can break in exactly one way.
/// </summary>
internal sealed class ContentWorkspace : IDisposable
{
    private readonly string m_root;

    public ContentWorkspace()
    {
        m_root = Path.Combine(Path.GetTempPath(), "evertorch-tools-tests", Guid.NewGuid().ToString("N"));
        ContentRoot = Path.Combine(m_root, "content");
        OutputDirectory = Path.Combine(m_root, "out");
        ClientDirectory = Path.Combine(m_root, "client-copy", "GameData");
        CopyDirectory(Path.Combine(FixturesDirectory, "valid"), ContentRoot);
    }

    public static string FixturesDirectory => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    public string ContentRoot { get; }

    public string OutputDirectory { get; }

    public string ClientDirectory { get; }

    public void Dispose()
    {
        if (Directory.Exists(m_root))
        {
            Directory.Delete(m_root, true);
        }
    }

    public void Replace(string relativePath, string oldText, string newText)
    {
        string path = Path.Combine(ContentRoot, relativePath);
        string text = File.ReadAllText(path);
        if (!text.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Fixture " + relativePath + " does not contain: " + oldText);
        }

        File.WriteAllText(path, text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    public void Write(string relativePath, string text)
    {
        string path = Path.Combine(ContentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(ContentRoot, relativePath));
    }

    public void Move(string relativePath, string newRelativePath)
    {
        string destination = Path.Combine(ContentRoot, newRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(Path.Combine(ContentRoot, relativePath), destination);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
}
