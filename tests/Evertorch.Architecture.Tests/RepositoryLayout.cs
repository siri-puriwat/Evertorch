using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
internal static class RepositoryLayout
{
    private const string SolutionFileName = "Evertorch.sln";

    // Unity generates and owns the .csproj files inside the client project.
    private const string UnityProjectName = "Evertorch.Client";

    public static string RootDirectory => FindRootDirectory();

    public static string ProjectFilePath(string projectName)
    {
        return Path.Combine(RootDirectory, projectName, projectName + ".csproj");
    }

    public static string AssemblyDefinitionPath(string projectName)
    {
        return Path.Combine(RootDirectory, projectName, projectName + ".asmdef");
    }

    public static IReadOnlyCollection<string> RuntimeProjectNames()
    {
        return Directory
            .GetDirectories(RootDirectory, "Evertorch.*")
            .Select(directory => Path.GetFileName(directory))
            .Where(name => name != UnityProjectName && File.Exists(ProjectFilePath(name)))
            .ToArray();
    }

    public static IReadOnlyCollection<string> ProjectReferences(string projectName)
    {
        return ItemIncludes(projectName, "ProjectReference")
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();
    }

    public static IReadOnlyCollection<string> PackageReferences(string projectName)
    {
        return ItemIncludes(projectName, "PackageReference").ToArray();
    }

    public static string PackageVersion(string projectName, string packageName)
    {
        return XDocument.Load(ProjectFilePath(projectName))
            .Descendants("PackageReference")
            .Where(item => (string?)item.Attribute("Include") == packageName)
            .Select(item => (string?)item.Attribute("Version") ?? string.Empty)
            .Single();
    }

    public static string ClientFilePath(string relativePath)
    {
        return Path.Combine(RootDirectory, UnityProjectName, relativePath);
    }

    private static IEnumerable<string> ItemIncludes(string projectName, string itemName)
    {
        var project = XDocument.Load(ProjectFilePath(projectName));
        return project
            .Descendants(itemName)
            .Select(item => (string?)item.Attribute("Include"))
            .Where(include => !string.IsNullOrEmpty(include))
            .Select(include => include!);
    }

    private static string FindRootDirectory()
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

        throw new InvalidOperationException(
            SolutionFileName + " was not found above " + TestContext.CurrentContext.TestDirectory + ".");
    }
}
}
