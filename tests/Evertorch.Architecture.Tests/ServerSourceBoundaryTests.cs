using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
/// <summary>
///     The transport library and ASP.NET Core are project-level dependencies of the server, so the assembly checks
///     cannot keep them out of the simulation. This reads the sources instead.
/// </summary>
[TestFixture]
public sealed class ServerSourceBoundaryTests
{
    private const string TransportLibrary = "LiteNetLib";
    private const string AdapterFolder = "Transport";
    private const string HealthFolder = "Health";

    // The namespace in a using directive or a qualified name. The adapter's own type name may appear anywhere.
    private static readonly Regex NamespaceUse = new(@"\bLiteNetLib\s*[;.]", RegexOptions.CultureInvariant);

    private static readonly Regex AspNetCoreUse = new(@"\bMicrosoft\.AspNetCore\b", RegexOptions.CultureInvariant);

    private static IEnumerable<string> ServerSources(out string serverRoot)
    {
        serverRoot = Path.Combine(RepositoryLayout.RootDirectory, "Evertorch.Server");
        return Directory.EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories);
    }

    private static bool IsInHealth(string serverRoot, string path)
    {
        string relative = Path.GetRelativePath(serverRoot, path).Replace('\\', '/');
        return relative.StartsWith($"{HealthFolder}/", StringComparison.Ordinal);
    }

    private static bool IsAdapter(string serverRoot, string path)
    {
        string relative = Path.GetRelativePath(serverRoot, path).Replace('\\', '/');
        return relative.StartsWith($"{AdapterFolder}/{TransportLibrary}", StringComparison.Ordinal);
    }

    [Test]
    public void ServerSources_InTheHealthFolder_UseAspNetCore()
    {
        IEnumerable<string> sources = ServerSources(out string serverRoot);

        bool isUsed = sources
            .Where(path => IsInHealth(serverRoot, path))
            .Any(path => AspNetCoreUse.IsMatch(File.ReadAllText(path)));

        Assert.That(isUsed, Is.True, "the guard above would pass vacuously if the endpoint moved");
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

    [Test]
    public void ServerSources_OutsideTheHealthFolder_DoNotUseAspNetCore()
    {
        IEnumerable<string> sources = ServerSources(out string serverRoot);

        var offenders = sources
            .Where(path => !IsInHealth(serverRoot, path))
            .Where(path => AspNetCoreUse.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(serverRoot, path))
            .ToList();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ServerSources_OutsideTransportAdapter_DoNotReferenceTransportLibrary()
    {
        string serverRoot = Path.Combine(RepositoryLayout.RootDirectory, "Evertorch.Server");

        var offenders = Directory
            .EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsAdapter(serverRoot, path))
            .Where(path => NamespaceUse.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(serverRoot, path))
            .ToList();

        Assert.That(offenders, Is.Empty);
    }
}
}
