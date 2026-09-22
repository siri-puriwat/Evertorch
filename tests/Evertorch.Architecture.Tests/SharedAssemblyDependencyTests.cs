using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
[TestFixture]
public sealed class SharedAssemblyDependencyTests
{
    private const string GameProject = "Evertorch.Game";
    private const string ProtocolProject = "Evertorch.Protocol";

    [TestCase(GameProject)]
    [TestCase(ProtocolProject)]
    public void CompiledAssembly_ForSharedProject_HasNoForbiddenReferences(string projectName)
    {
        // Loaded by name rather than typeof: the check must hold even while a shared assembly declares no types.
        var assembly = Assembly.Load(new AssemblyName(projectName));

        string[] forbidden = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(ForbiddenDependencies.IsForbiddenForSharedAssembly)
            .ToArray();

        Assert.That(forbidden, Is.Empty);
    }

    // The compiler drops unused references from metadata, so declared dependencies are checked separately.
    [TestCase(GameProject)]
    [TestCase(ProtocolProject)]
    public void ProjectFile_ForSharedProject_DeclaresNoForbiddenDependencies(string projectName)
    {
        string[] forbidden = RepositoryLayout.PackageReferences(projectName)
            .Concat(RepositoryLayout.ProjectReferences(projectName))
            .Where(ForbiddenDependencies.IsForbiddenForSharedAssembly)
            .ToArray();

        Assert.That(forbidden, Is.Empty);
    }

    [TestCase(GameProject)]
    [TestCase(ProtocolProject, GameProject)]
    public void AssemblyDefinition_ForSharedProject_ReferencesOnlyAllowedAssemblies(
        string projectName,
        params string[] allowedReferences)
    {
        using var assemblyDefinition = JsonDocument.Parse(
            File.ReadAllText(RepositoryLayout.AssemblyDefinitionPath(projectName)));
        JsonElement root = assemblyDefinition.RootElement;

        Assert.That(root.GetProperty("noEngineReferences").GetBoolean(), Is.True);
        Assert.That(StringArray(root, "references"), Is.EquivalentTo(allowedReferences));
        Assert.That(StringArray(root, "precompiledReferences"), Is.Empty);
    }

    private static IReadOnlyCollection<string> StringArray(JsonElement root, string propertyName)
    {
        return root
            .GetProperty(propertyName)
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();
    }
}
}
