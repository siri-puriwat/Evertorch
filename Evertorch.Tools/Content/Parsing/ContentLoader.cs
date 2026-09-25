using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Evertorch.Tools
{
/// <summary>
///     Parses canonical YAML under a content root into the normalized model. The folder decides the definition kind.
/// </summary>
public static class ContentLoader
{
    private const string ItemsFolder = "items";
    private const string MonstersFolder = "monsters";
    private const string SkillsFolder = "skills";
    private const string JobsFolder = "jobs";
    private const string MapsFolder = "maps";
    private const string ExperienceFolder = "experience";
    private const string StatusEffectsFolder = "status-effects";

    public static ContentSet Load(string contentRoot, List<ContentDiagnostic> diagnostics)
    {
        var items = new List<AuthoredItem>();
        var monsters = new List<AuthoredMonster>();
        var skills = new List<AuthoredSkill>();
        var jobs = new List<AuthoredJob>();
        var maps = new List<AuthoredMap>();
        var experienceTables = new List<AuthoredExperienceTable>();
        var statusEffects = new List<AuthoredStatusEffect>();
        var declaredIds = new HashSet<string>(StringComparer.Ordinal);

        if (!Directory.Exists(contentRoot))
        {
            diagnostics.Add(new ContentDiagnostic(".", string.Empty, 0, "content directory does not exist"));
            return new ContentSet(items, monsters, skills, jobs, maps, experienceTables, statusEffects, declaredIds);
        }

        foreach (string file in EnumerateFiles(contentRoot))
        {
            string relativePath = Path.GetRelativePath(contentRoot, file).Replace('\\', '/');
            string folder = relativePath.Contains('/', StringComparison.Ordinal)
                ? relativePath.Substring(0, relativePath.IndexOf('/', StringComparison.Ordinal))
                : string.Empty;

            if (!string.Equals(Path.GetExtension(file), ".yml", StringComparison.Ordinal))
            {
                diagnostics.Add(
                    new ContentDiagnostic(relativePath, string.Empty, 0, "content files must use the .yml extension"));
                continue;
            }

            YamlFieldReader? root = ReadRoot(file, relativePath, diagnostics);
            if (root == null)
            {
                continue;
            }

            switch (folder)
            {
                case ItemsFolder:
                    AddIfValid(items, ItemDefinitionReader.Read(root, diagnostics, declaredIds));
                    break;
                case MonstersFolder:
                    AddIfValid(monsters, MonsterDefinitionReader.Read(root, diagnostics, declaredIds));
                    break;
                case SkillsFolder:
                    AddIfValid(skills, SkillDefinitionReader.Read(root, diagnostics, declaredIds));
                    break;
                case JobsFolder:
                    AddIfValid(jobs, JobDefinitionReader.Read(root, diagnostics, declaredIds));
                    break;
                case MapsFolder:
                    AddIfValid(maps, MapDefinitionReader.Read(root, diagnostics, declaredIds));
                    break;
                case ExperienceFolder:
                    AddIfValid(experienceTables, ExperienceTableReader.Read(root, diagnostics, declaredIds));
                    break;
                case StatusEffectsFolder:
                    AddIfValid(statusEffects, StatusEffectReader.Read(root, diagnostics, declaredIds));
                    break;
                default:
                    diagnostics.Add(
                        new ContentDiagnostic(
                            relativePath,
                            string.Empty,
                            0,
                            "file is not inside a known definition folder (items, monsters, skills, jobs, maps, "
                            + "experience, status-effects)"));
                    break;
            }
        }

        return new ContentSet(items, monsters, skills, jobs, maps, experienceTables, statusEffects, declaredIds);
    }

    private static IEnumerable<string> EnumerateFiles(string contentRoot)
    {
        return Directory
            .EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(contentRoot, path).Replace('\\', '/'), StringComparer.Ordinal);
    }

    private static void AddIfValid<T>(List<T> definitions, T? definition)
        where T : class
    {
        if (definition != null)
        {
            definitions.Add(definition);
        }
    }

    private static YamlFieldReader? ReadRoot(string file, string relativePath, List<ContentDiagnostic> diagnostics)
    {
        var stream = new YamlStream();
        try
        {
            using (var reader = new StreamReader(file))
            {
                stream.Load(reader);
            }
        }
        catch (YamlException exception)
        {
            diagnostics.Add(
                new ContentDiagnostic(
                    relativePath,
                    string.Empty,
                    (int)exception.Start.Line,
                    $"invalid YAML: {exception.Message}"));
            return null;
        }

        if (stream.Documents.Count != 1 || !(stream.Documents[0].RootNode is YamlMappingNode mapping))
        {
            diagnostics.Add(
                new ContentDiagnostic(
                    relativePath,
                    string.Empty,
                    0,
                    "file must contain exactly one definition mapping"));
            return null;
        }

        return YamlFieldReader.ForRoot(mapping, relativePath, diagnostics);
    }
}
}
