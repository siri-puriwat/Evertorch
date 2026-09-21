using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
/// <summary>
/// The transport library is a project-level dependency of the server, so the assembly checks cannot keep it out of
/// the simulation. This reads the sources instead.
/// </summary>
[TestFixture]
public sealed class ServerSourceBoundaryTests
{
    private const string TransportLibrary = "LiteNetLib";
    private const string AdapterFolder = "Transport";

    // The namespace in a using directive or a qualified name. The adapter's own type name may appear anywhere.
    private static readonly Regex NamespaceUse = new Regex(@"\bLiteNetLib\s*[;.]", RegexOptions.CultureInvariant);

    [Test]
    public void ServerSources_OutsideTransportAdapter_DoNotReferenceTransportLibrary()
    {
        string serverRoot = Path.Combine(RepositoryLayout.RootDirectory, "Evertorch.Server");

        List<string> offenders = Directory
            .EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsAdapter(serverRoot, path))
            .Where(path => NamespaceUse.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(serverRoot, path))
            .ToList();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ServerSources_InTransportAdapter_Exist()
    {
        string serverRoot = Path.Combine(RepositoryLayout.RootDirectory, "Evertorch.Server");

        IEnumerable<string> adapters = Directory
            .EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => IsAdapter(serverRoot, path));

        Assert.That(adapters, Is.Not.Empty, "the guard above would pass vacuously if the adapter moved");
    }

    private static bool IsAdapter(string serverRoot, string path)
    {
        string relative = Path.GetRelativePath(serverRoot, path).Replace('\\', '/');
        return relative.StartsWith(AdapterFolder + "/" + TransportLibrary, StringComparison.Ordinal);
    }
}
}
