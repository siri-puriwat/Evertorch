using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
[TestFixture]
public sealed class ClientAssemblyReferenceTests
{
    private const string ClientAssembly = "Assets/_ProjectScripts.Evertorch.Client/Evertorch.Client.asmdef";
    private const string EditModeTests = "Assets/_Project/Tests/EditMode/Evertorch.Client.Tests.EditMode.asmdef";
    private const string PlayModeTests = "Assets/_Project/Tests/PlayMode/Evertorch.Client.Tests.PlayMode.asmdef";

    private static readonly string[] ServerSideAssemblies =
    {
        "Evertorch.Rules", "Evertorch.Server", "Evertorch.Persistence", "Evertorch.Tools"
    };

    [TestCase(ClientAssembly)]
    [TestCase(EditModeTests)]
    [TestCase(PlayModeTests)]
    public void ClientSideAssembly_NeverReferencesAServerSideAssembly(string assemblyDefinition)
    {
        Assert.That(References(assemblyDefinition).Intersect(ServerSideAssemblies), Is.Empty);
    }

    [TestCase(EditModeTests)]
    [TestCase(PlayModeTests)]
    public void ClientTestAssembly_ReachesTheTransportOnlyThroughTheClient(string assemblyDefinition)
    {
        Assert.That(References(assemblyDefinition), Does.Not.Contain("LiteNetLib"));
        Assert.That(References(assemblyDefinition), Does.Contain("Evertorch.Client"));
    }

    private static IReadOnlyCollection<string> References(string assemblyDefinition)
    {
        return StringArray(assemblyDefinition, "references");
    }

    private static IReadOnlyCollection<string> PrecompiledReferences(string assemblyDefinition)
    {
        return StringArray(assemblyDefinition, "precompiledReferences");
    }

    private static IReadOnlyCollection<string> StringArray(string assemblyDefinition, string propertyName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(RepositoryLayout.ClientFilePath(assemblyDefinition)));
        return document.RootElement
            .GetProperty(propertyName)
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();
    }

    [Test]
    public void ClientAssembly_ReferencesOnlyTheSharedPackagesTransportAndUnityModules()
    {
        Assert.That(
            References(ClientAssembly),
            Is.EquivalentTo(
                new[]
                {
                    "Evertorch.Game",
                    "Evertorch.Protocol",
                    "LiteNetLib",
                    "Unity.InputSystem",
                    "Unity.TextMeshPro",
                    "UnityEngine.UI"
                }));
        Assert.That(PrecompiledReferences(ClientAssembly), Is.Empty);
    }

    [Test]
    public void UnityManifest_PinsLiteNetLibToTheSameReleaseAsTheServer()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(RepositoryLayout.ClientFilePath("Packages/manifest.json")));
        string? source = manifest.RootElement
            .GetProperty("dependencies")
            .GetProperty("com.revenantx.litenetlib")
            .GetString();

        string serverVersion = RepositoryLayout.PackageVersion("Evertorch.Server", "LiteNetLib");

        Assert.That(source, Does.EndWith("#" + serverVersion + "-upm"));
    }
}
}
