using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Ashen Quarry kept out of the seeded hosts (Coding Standards §10): it sorts before every other map, so its
///     monsters would spawn first and shift every seeded scenario, and a host given a seed loads the repository's package
///     without the quarry's spawns unless its test asks for them.
/// </summary>
[TestFixture]
public sealed class SeededPackageTests
{
    private const string Quarry = "map.ashen_quarry";

    private static JsonArray Maps(IReadOnlyDictionary<string, byte[]> files)
    {
        return JsonNode.Parse(files["maps.json"])!["definitions"]!.AsArray();
    }

    [Test]
    public void BuildSeededPackage_TakesTheQuarrysSpawnsOff_AndNothingElse()
    {
        Dictionary<string, byte[]> raw = PackageFixture.BuildRepositoryPackage();
        Dictionary<string, byte[]> seeded = PackageFixture.BuildSeededPackage();
        Dictionary<string, byte[]> withSpawns = PackageFixture.BuildSeededPackage(true);

        JsonArray rawMaps = Maps(raw);
        JsonArray seededMaps = Maps(seeded);
        JsonNode quarry = seededMaps.Single(node => (string?)node!["id"] == Quarry)!;
        rawMaps.Single(node => (string?)node!["id"] == Quarry)!["monsterSpawns"] = new JsonArray();

        Assert.That(quarry["monsterSpawns"]!.AsArray(), Is.Empty, "the quarry spawns nothing");
        Assert.That(seededMaps.ToJsonString(), Is.EqualTo(rawMaps.ToJsonString()), "every other map as it was");
        Assert.That(
            seeded.Keys.Where(file => file != "maps.json" && file != "manifest.json")
                .Where(file => !seeded[file].SequenceEqual(raw[file])),
            Is.Empty,
            "every other file as it was");
        Assert.That(
            PackageFixture.ClientContentVersionOf(seeded),
            Is.EqualTo(PackageFixture.ClientContentVersionOf(raw)),
            "the real client still connects");
        Assert.That(withSpawns.Keys.All(file => withSpawns[file].SequenceEqual(raw[file])), Is.True, "untouched");
        Assert.That(ServerContentLoader.Load(seeded).Maps.ContainsKey(new MapDefinitionId(Quarry)), Is.True);
    }

    // A test that gives a host a seed must load the seeded package, or the Umbral Grotto's test package built on it,
    // so no later map's spawns shift its draws.
    [Test]
    public void EveryTestWithASeededHost_LoadsTheSeededPackage()
    {
        string tests = Path.Combine(
            Path.GetDirectoryName(PackageFixture.RepositoryContentDirectory)!,
            "tests",
            "Evertorch.Server.Tests");
        string[] seededHosts = Directory.GetFiles(tests, "*.cs")
            .Where(file => Path.GetFileName(file) != $"{nameof(SeededPackageTests)}.cs")
            .Where(file => File.ReadAllText(file).Contains("World:RandomSeed=", StringComparison.Ordinal))
            .ToArray();

        Assert.That(seededHosts, Is.Not.Empty);
        Assert.That(
            seededHosts.Where(file =>
                {
                    string text = File.ReadAllText(file);
                    return text.Contains("BuildRepositoryPackage()", StringComparison.Ordinal)
                        || !(text.Contains("BuildSeededPackage(", StringComparison.Ordinal)
                            || text.Contains("DungeonPackage.Build(", StringComparison.Ordinal));
                })
                .Select(Path.GetFileName),
            Is.Empty);
    }

    // The in-memory host keeps the quarry out of every map unless a test asks for it.
    [Test]
    public void TheTestServer_LeavesTheQuarryOutOfEveryMap_UnlessATestAsksForIt()
    {
        var quarry = new MapDefinitionId(Quarry);

        Assert.That(new TestServer(withEveryMap: true).World.TryGetMap(quarry, out _), Is.False);
        Assert.That(new TestServer(withEveryMap: true, withQuarry: true).World.TryGetMap(quarry, out _), Is.True);
    }
}
}
