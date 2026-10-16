using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Ashen Quarry kept out of the seeded hosts (Coding Standards §10): it sorts before every other map, so its
///     monsters would spawn first and shift every seeded scenario, and a host given a seed loads the repository's package
///     without the quarry's spawns unless its test asks for them; a seeded world on that package is the world without
///     the quarry, entity for entity and draw for draw.
/// </summary>
[TestFixture]
public sealed class SeededPackageTests
{
    private const string Quarry = "map.ashen_quarry";

    private static JsonArray Maps(IReadOnlyDictionary<string, byte[]> files)
    {
        return JsonNode.Parse(files["maps.json"])!["definitions"]!.AsArray();
    }

    // The repository's package with neither the quarry nor the field's portal to it.
    private static Dictionary<string, byte[]> WithoutTheQuarry()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        JsonArray definitions = Maps(files);
        definitions.Remove(definitions.Single(node => (string?)node!["id"] == Quarry));
        JsonArray portals = definitions.Single(node => (string?)node!["id"] == "map.training_field")!["portals"]!
            .AsArray();
        portals.Remove(portals.Single(node => (string?)node!["destination"]!["map"] == Quarry));
        files["maps.json"] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);
        return files;
    }

    // A seeded world of every map: outside the quarry, each monster and NPC by its map, entity ID, definition, and
    // place, then the world's next ten draws.
    private static string Describe(Dictionary<string, byte[]> files, bool withQuarry)
    {
        var server = new TestServer(
            content: ServerContentLoader.Load(files),
            withEveryMap: true,
            withGrotto: true,
            withQuarry: withQuarry,
            withMonsters: true,
            withNpcs: true,
            withMonsterAi: false,
            randomSeed: 13);
        IEnumerable<string> entities = server.World.Maps
            .Where(map => map.Definition.Id.Value != Quarry)
            .SelectMany(map => map.Monsters.Cast<WorldEntity>()
                .Concat(map.Npcs)
                .Select(entity => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1} {2} {3:R},{4:R}",
                    map.Definition.Id.Value,
                    entity.Id.Value,
                    entity.DefinitionId,
                    entity.Position.X,
                    entity.Position.Z)));
        IEnumerable<string> draws = Enumerable.Range(0, 10)
            .Select(_ => server.Random.Next(1_000_000).ToString(CultureInfo.InvariantCulture));
        return string.Join("\n", entities.Concat(draws));
    }

    // The isolation proved (Milestone 21 line 6): a seeded world on the seeded package numbers, places, and draws as one
    // on a package without the quarry at all, while the quarry's spawns would renumber it.
    [Test]
    public void ASeededWorld_OnTheSeededPackage_NumbersPlacesAndDrawsAsOneWithoutTheQuarry()
    {
        string seeded = Describe(PackageFixture.BuildSeededPackage(), true);
        string without = Describe(WithoutTheQuarry(), false);
        string withSpawns = Describe(PackageFixture.BuildSeededPackage(true), true);

        Assert.That(seeded, Is.EqualTo(without));
        Assert.That(withSpawns, Is.Not.EqualTo(without), "the quarry's spawns would shift it");
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

    // The packages other tests build on start from the seeded package, so a seeded host on any of them keeps its draws.
    [Test]
    public void ThePackagesTestsBuildOn_LeaveTheQuarrysSpawnsOut()
    {
        var packages = new Dictionary<string, Dictionary<string, byte[]>>
        {
            ["FeePackage"] = FeePackage.Build(12),
            ["GearPackage"] = GearPackage.Build(),
            ["DungeonPackage"] = DungeonPackage.Build()
        };

        Assert.That(
            packages.Where(package => Maps(package.Value)
                    .Single(node => (string?)node!["id"] == Quarry)!["monsterSpawns"]!.AsArray()
                    .Count > 0)
                .Select(package => package.Key),
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
