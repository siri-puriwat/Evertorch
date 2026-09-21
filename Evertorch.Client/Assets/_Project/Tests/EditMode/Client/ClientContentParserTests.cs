using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ClientContentParserTests
{
    private const string Maps =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"map.training_ground\",\"displayName\":\"Training Ground\","
        + "\"scene\":\"map_training_ground\",\"navigation\":{\"cellSize\":1,\"originX\":-1,\"originZ\":-2,"
        + "\"agentRadius\":0.3,\"maxStepHeight\":0.4,\"columns\":3,\"rows\":2,\"legend\":["
        + "{\"symbol\":\"a\",\"surface\":\"wall\",\"axis\":\"none\",\"heightAtMin\":0,\"heightAtMax\":0},"
        + "{\"symbol\":\"b\",\"surface\":\"floor\",\"axis\":\"none\",\"heightAtMin\":0,\"heightAtMax\":0},"
        + "{\"symbol\":\"c\",\"surface\":\"floor\",\"axis\":\"x\",\"heightAtMin\":0,\"heightAtMax\":0.25}],"
        + "\"cellRows\":[\"abc\",\"bba\"]}}]}";

    [Test]
    public void Parse_ValidPackage_BuildsTheMapAndItsGrid()
    {
        Package package = new Package(Maps);

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(error, Is.Empty);
        Assert.That(content, Is.Not.Null);
        Assert.That(content!.Version, Is.EqualTo(package.Version));
        Assert.That(content.TryGetMap(new MapDefinitionId("map.training_ground"), out ClientMap? map), Is.True);
        Assert.That(map!.DisplayName, Is.EqualTo("Training Ground"));
        Assert.That(map.SceneKey, Is.EqualTo("map_training_ground"));
        NavigationGrid grid = map.Navigation;
        Assert.That(grid.Columns, Is.EqualTo(3));
        Assert.That(grid.Rows, Is.EqualTo(2));
        Assert.That(grid.OriginX, Is.EqualTo(-1f));
        Assert.That(grid.OriginZ, Is.EqualTo(-2f));
        Assert.That(grid.GetCell(0, 0).Surface, Is.EqualTo(NavigationSurface.Wall), "row 0 is the southern edge");
        Assert.That(grid.GetCell(2, 0).Axis, Is.EqualTo(RampAxis.X));
        Assert.That(grid.GetCell(2, 0).HeightAtMax, Is.EqualTo(0.25f));
        Assert.That(grid.GetCell(2, 1).Surface, Is.EqualTo(NavigationSurface.Wall));
    }

    [Test]
    public void Parse_ValidPackage_ServesTheGridThroughTheMapProvider()
    {
        Package package = new Package(Maps);
        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string _);

        bool found = content!.TryGetNavigation(new MapDefinitionId("map.training_ground"), out NavigationGrid? grid);
        bool missing = content.TryGetNavigation(new MapDefinitionId("map.elsewhere"), out NavigationGrid? none);

        Assert.That(found, Is.True);
        Assert.That(grid, Is.Not.Null);
        Assert.That(missing, Is.False);
        Assert.That(none, Is.Null);
    }

    [Test]
    public void Parse_WhenAFileWasEdited_IsRefused()
    {
        Package package = new Package(Maps);
        package.Files[ClientContentParser.MapsFile] = Encoding.UTF8.GetBytes(Maps.Replace("\"abc\"", "\"bbc\""));

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain("maps.json").And.Contain("does not match"));
    }

    [Test]
    public void Parse_WhenAListedFileIsAbsent_IsRefused()
    {
        Package package = new Package(Maps);
        package.Files.Remove("items.json");

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain("items.json").And.Contain("missing"));
    }

    [Test]
    public void Parse_WhenTheVersionDoesNotBelongToTheFiles_IsRefused()
    {
        Package package = new Package(Maps, "0123456789abcdef");

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain("content version"));
    }

    [TestCase("not json at all")]
    [TestCase("{\"schemaVersion\":1,\"clientContentVersion\":\"x\",\"files\":[]}")]
    public void ReadFileList_ForAnUnreadableOrEmptyManifest_IsEmptyWithAnError(string manifest)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(manifest);

        IReadOnlyList<string> names = ClientContentParser.ReadFileList(bytes, out string error);

        Assert.That(names, Is.Empty);
        Assert.That(error, Is.Not.Empty);
    }

    [TestCase(2, "maps.json")]
    [TestCase(1, "../maps.json")]
    [TestCase(1, "sub/maps.json")]
    [TestCase(1, "C:maps.json")]
    [TestCase(1, "manifest.json")]
    [TestCase(1, "")]
    public void ReadFileList_ForAnUnsupportedSchemaOrUnsafeFileName_IsEmptyWithAnError(int schema, string fileName)
    {
        string manifest = "{\"schemaVersion\":" + schema + ",\"clientContentVersion\":\"x\",\"files\":[{\"path\":\""
            + fileName + "\",\"sha256\":\"\"}]}";

        byte[] bytes = Encoding.UTF8.GetBytes(manifest);

        IReadOnlyList<string> names = ClientContentParser.ReadFileList(bytes, out string error);

        Assert.That(names, Is.Empty);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void ReadFileList_ForAValidManifest_ListsItsFiles()
    {
        Package package = new Package(Maps);

        IReadOnlyList<string> names = ClientContentParser.ReadFileList(package.Manifest, out string error);

        Assert.That(error, Is.Empty);
        Assert.That(names, Is.EquivalentTo(new[] { "items.json", "maps.json" }));
    }

    [TestCase("\"surface\":\"wall\"", "\"surface\":\"lava\"", "legend")]
    [TestCase("\"axis\":\"x\"", "\"axis\":\"y\"", "legend")]
    [TestCase("\"symbol\":\"b\"", "\"symbol\":\"a\"", "legend")]
    [TestCase("\"abc\"", "\"abz\"", "outside the legend")]
    [TestCase("\"abc\"", "\"ab\"", "wrong length")]
    [TestCase("\"rows\":2", "\"rows\":3", "do not match")]
    [TestCase("\"heightAtMax\":0.25", "\"heightAtMax\":5", "steep")]
    [TestCase("\"id\":\"map.training_ground\"", "\"id\":\"item.potion\"", "map ID")]
    [TestCase("\"schemaVersion\":1,\"definitions\"", "\"schemaVersion\":9,\"definitions\"", "schema version")]
    public void Parse_ForMalformedMaps_IsRefusedWithAReason(string oldText, string newText, string expected)
    {
        Assert.That(Maps, Does.Contain(oldText));
        Package package = new Package(Maps.Replace(oldText, newText));

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain(expected));
    }

    [Test]
    public void Parse_TheGeneratedPackageInStreamingAssets_YieldsTheTrainingGround()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, StreamingContentLoader.FolderName);
        string manifestPath = Path.Combine(folder, ClientContentParser.ManifestFile);
        if (!File.Exists(manifestPath))
        {
            Assert.Ignore("No generated client package is present. " + StreamingContentLoader.MissingPackageHint);
        }

        byte[] manifest = File.ReadAllBytes(manifestPath);
        Dictionary<string, byte[]> files = ClientContentParser
            .ReadFileList(manifest, out string _)
            .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(folder, name)));

        ClientContent? content = ClientContentParser.Parse(manifest, files, out string error);

        Assert.That(error, Is.Empty);
        Assert.That(content!.TryGetMap(new MapDefinitionId("map.training_ground"), out ClientMap? map), Is.True);
        Assert.That(map!.Navigation.Columns, Is.EqualTo(48));
        Assert.That(map.Navigation.CanOccupy(0f, 0f), Is.True, "players spawn at the origin");
        Assert.That(MapSceneResolver.TryResolve(map.SceneKey, out string _), Is.True);
    }

    private sealed class Package
    {
        public Package(string maps, string? versionOverride = null)
        {
            Files = new Dictionary<string, byte[]>
            {
                ["items.json"] = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"definitions\":[]}"),
                [ClientContentParser.MapsFile] = Encoding.UTF8.GetBytes(maps),
            };

            StringBuilder listing = new StringBuilder();
            StringBuilder entries = new StringBuilder();
            foreach (string name in Files.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                string hash = Hash(Files[name]);
                listing.Append(name).Append(':').Append(hash).Append('\n');
                entries.Append(entries.Length == 0 ? string.Empty : ",");
                entries.Append("{\"path\":\"").Append(name).Append("\",\"sha256\":\"").Append(hash).Append("\"}");
            }

            Version = versionOverride ?? Hash(Encoding.UTF8.GetBytes(listing.ToString())).Substring(0, 16);
            Manifest = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"clientContentVersion\":\"" + Version + "\",\"files\":[" + entries + "]}");
        }

        public Dictionary<string, byte[]> Files { get; }

        public byte[] Manifest { get; }

        public string Version { get; }

        private static string Hash(byte[] content)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(sha256.ComputeHash(content).Select(value => value.ToString("x2")));
            }
        }
    }
}
}
