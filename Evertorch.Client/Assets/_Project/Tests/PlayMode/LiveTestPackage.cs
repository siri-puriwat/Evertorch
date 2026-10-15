using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A copy of the repository's server package with server-only values of one monster changed, or skills taken off
///     monsters, signed again with the same client content version, so the real client still connects to the server
///     that loads it (Coding Standards §10). The copy lives in a temporary folder until it is disposed.
/// </summary>
public sealed class LiveTestPackage : IDisposable
{
    private const string ManifestFile = "manifest.json";

    /// <summary>
    ///     Milestone 19's monster skills, which the classes written before them take off, so their seeded fights draw as
    ///     they did; the riders of Spark Bolt and Grave Bite still draw.
    /// </summary>
    public static readonly IReadOnlyList<(string Monster, string Skill)> AilmentSkills = new[]
    {
        ("monster.forest_crawler", "skill.venom_bite"), ("monster.grotto_crawler", "skill.crushing_bite"),
        ("monster.gloom_wisp", "skill.hush"), ("monster.hollow_wisp", "skill.lull"),
        ("monster.slime_monarch", "skill.frost_breath")
    };

    private LiveTestPackage(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }

    /// <summary>
    ///     Copies the repository's server package and takes each named skill off its monster's list.
    /// </summary>
    public static LiveTestPackage WithoutSkills(IReadOnlyList<(string Monster, string Skill)> skills)
    {
        return WithMonster(null, new Dictionary<string, string>(), skills);
    }

    /// <summary>
    ///     Copies the repository's server package, sets each named field of <paramref name="monster" /> to its new JSON
    ///     value, and takes each of <paramref name="skills" /> off its monster's list.
    /// </summary>
    public static LiveTestPackage WithMonster(
        string? monster,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<(string Monster, string Skill)>? skills = null)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "evertorch-live-package-" + Guid.NewGuid().ToString("N"));
        var package = new LiveTestPackage(path);
        Directory.CreateDirectory(path);
        foreach (string file in Directory.GetFiles(LiveServer.ContentPath))
        {
            File.Copy(file, System.IO.Path.Combine(path, System.IO.Path.GetFileName(file)));
        }

        string monstersPath = System.IO.Path.Combine(path, "monsters.json");
        string json = File.ReadAllText(monstersPath);
        if (monster != null)
        {
            json = Edit(json, monster, fields);
        }

        foreach ((string owner, string skill) in skills ?? Array.Empty<(string, string)>())
        {
            json = WithoutSkill(json, owner, skill);
        }

        File.WriteAllText(monstersPath, json);
        package.Sign();
        return package;
    }

    // Takes the skill's object out of the monster's list with the comma that joins it to a neighbour.
    private static string WithoutSkill(string json, string monster, string skill)
    {
        int definition = json.IndexOf($"\"id\": \"{monster}\"", StringComparison.Ordinal);
        int named = definition < 0 ? -1 : json.IndexOf($"\"skill\": \"{skill}\"", definition, StringComparison.Ordinal);
        if (named < 0)
        {
            throw new InvalidOperationException($"'{monster}' tries no '{skill}'.");
        }

        int start = json.LastIndexOf('{', named);
        int end = json.IndexOf('}', named) + 1;
        int before = start - 1;
        while (char.IsWhiteSpace(json[before]))
        {
            before--;
        }

        int after = end;
        while (char.IsWhiteSpace(json[after]))
        {
            after++;
        }

        if (json[before] == ',')
        {
            return json.Remove(before, end - before);
        }

        return json[after] == ',' ? json.Remove(start, after + 1 - start) : json.Remove(start, end - start);
    }

    // The tools write one field to a line; each is changed within the monster's own definition.
    private static string Edit(string json, string monster, IReadOnlyDictionary<string, string> fields)
    {
        int definition = json.IndexOf($"\"id\": \"{monster}\"", StringComparison.Ordinal);
        if (definition < 0)
        {
            throw new InvalidOperationException($"The server package defines no '{monster}'.");
        }

        var edited = new StringBuilder(json);
        foreach (KeyValuePair<string, string> field in fields)
        {
            string text = edited.ToString();
            int start = text.IndexOf($"\"{field.Key}\": ", definition, StringComparison.Ordinal);
            int end = start < 0 ? -1 : text.IndexOfAny(new[] { ',', '\n' }, start);
            if (end < 0)
            {
                throw new InvalidOperationException($"'{monster}' has no field '{field.Key}'.");
            }

            edited.Remove(start, end - start).Insert(start, $"\"{field.Key}\": {field.Value}");
        }

        return edited.ToString();
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static string ClientContentVersionOf(string manifest)
    {
        const string key = "\"clientContentVersion\": \"";
        int start = manifest.IndexOf(key, StringComparison.Ordinal) + key.Length;
        return manifest.Substring(start, manifest.IndexOf('"', start) - start);
    }

    // As the tools sign a package: each data file's hash, then the server version from the listing of them all.
    private void Sign()
    {
        string manifestPath = System.IO.Path.Combine(Path, ManifestFile);
        string clientVersion = ClientContentVersionOf(File.ReadAllText(manifestPath));
        string[] files = Directory.GetFiles(Path)
            .Select(System.IO.Path.GetFileName)
            .Where(name => name != ManifestFile)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var listing = new StringBuilder();
        var entries = new List<string>();
        foreach (string name in files)
        {
            string hash = Hash(File.ReadAllBytes(System.IO.Path.Combine(Path, name)));
            listing.Append(name).Append(':').Append(hash).Append('\n');
            entries.Add($"{{\"path\":\"{name}\",\"sha256\":\"{hash}\"}}");
        }

        string serverVersion = Hash(Encoding.UTF8.GetBytes(listing.ToString())).Substring(0, 16);
        File.WriteAllText(
            manifestPath,
            "{\"schemaVersion\":1,"
            + $"\"serverContentVersion\":\"{serverVersion}\","
            + $"\"clientContentVersion\":\"{clientVersion}\","
            + $"\"files\":[{string.Join(",", entries)}]}}");
    }
}
}
