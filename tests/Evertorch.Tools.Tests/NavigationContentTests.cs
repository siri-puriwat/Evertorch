using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
[TestFixture]
public sealed class NavigationContentTests
{
    private const string Map = "maps/training_ground.yml";
    private const string ObstacleEntry = "{ symbol: \"o\", surface: obstacle, height: 0.0 }";
    private const string GateEntry = "{ symbol: \"G\", surface: gate, height: 0.0 }";
    private const string LowerRamp = "ramp: { axis: z, from: 0.0, to: 0.5 }";
    private const string UpperRamp = "ramp: { axis: z, from: 0.5, to: 1.0 }";
    private const string MarkerRow = "\"#.............#...N....................#\"";
    private const string SpawnPosition = "position: { x: 3.4375, y: 0.0, z: -7.5625 }";
    private const string MonsterCenter = "center: { x: 12.6875, y: 0.0, z: 14.3125 }";

    [TestCase("cellSize: 1.0", "cellSize: 0", "navigation.cellSize", "greater than 0")]
    [TestCase("cellSize: 1.0", "cellSize: wide", "navigation.cellSize", "finite number")]
    [TestCase("agentRadius: 0.3", "agentRadius: -1", "navigation.agentRadius", "greater than 0")]
    [TestCase("maxStepHeight: 0.4", "maxStepHeight: -0.1", "navigation.maxStepHeight", "between 0 and")]
    [TestCase("origin: { x: -20.0, z: -20.0 }", "origin: { x: -20.0 }", "navigation.origin.z", "required field")]
    [TestCase(
        ObstacleEntry,
        "{ symbol: \"oo\", surface: obstacle, height: 0.0 }",
        "navigation.legend[3].symbol",
        "single")]
    [TestCase(
        ObstacleEntry,
        "{ symbol: \"#\", surface: obstacle, height: 0.0 }",
        "navigation.legend[3].symbol",
        "already")]
    [TestCase(ObstacleEntry, "{ symbol: \"o\", surface: lava, height: 0.0 }", "navigation.legend[3].surface", "one of")]
    [TestCase(GateEntry, "{ symbol: \"G\", surface: gate }", "navigation.legend[5].height", "exactly one of")]
    [TestCase(
        GateEntry,
        "{ symbol: \"G\", surface: gate, height: 0.0, ramp: { axis: x, from: 0.0, to: 0.1 } }",
        "navigation.legend[5].ramp",
        "exactly one of")]
    [TestCase(
        GateEntry,
        "{ symbol: \"G\", surface: gate, height: 0.0, cost: 3 }",
        "navigation.legend[5].cost",
        "unknown")]
    [TestCase(
        "surface: floor, " + LowerRamp,
        "surface: wall, " + LowerRamp,
        "navigation.legend[6].surface",
        "only floor")]
    [TestCase(
        LowerRamp,
        "ramp: { axis: none, from: 0.0, to: 0.5 }",
        "navigation.legend[6].ramp.axis",
        "must be x or z")]
    [TestCase(UpperRamp, "ramp: { axis: z, from: 0.5, to: 9.0 }", "navigation.legend[7].ramp.to", "too steep")]
    [TestCase(MarkerRow, "\"#.............#...?....................#\"", "navigation.rows[23]", "'?' at position 19")]
    [TestCase(
        MarkerRow,
        "\"#.............#...N...#\"",
        "navigation.rows[23]",
        "has 23 symbols but the first row has 40")]
    [TestCase(SpawnPosition, "position: { x: -5.5, y: 0.0, z: -7.5 }", "server.spawnPoint.position", "stand")]
    [TestCase(SpawnPosition, "position: { x: -4.9, y: 0.0, z: -7.5 }", "server.spawnPoint.position", "stand")]
    [TestCase(SpawnPosition, "position: { x: 300.0, y: 0.0, z: -7.5 }", "server.spawnPoint.position", "stand")]
    [TestCase(
        SpawnPosition,
        "position: { x: 3.4375, y: 2.0, z: -7.5625 }",
        "server.spawnPoint.position",
        "ground height 0")]
    [TestCase(
        MonsterCenter,
        "center: { x: 11.5, y: 0.0, z: -13.5 }",
        "server.monsterSpawns[0].center",
        "cannot be reached")]
    [TestCase(MonsterCenter, "center: { x: 10.5, y: 0.0, z: -13.5 }", "server.monsterSpawns[0].center", "stand")]
    [TestCase(
        MonsterCenter,
        "center: { x: -13.0, y: 0.0, z: 13.0 }",
        "server.monsterSpawns[0].center",
        "ground height 1")]
    public void Run_WhenNavigationOrPlacementIsBroken_ReportsThatFieldAndLine(
        string oldText,
        string newText,
        string expectedFieldPath,
        string expectedMessagePart)
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            workspace.Replace(Map, oldText, newText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Packages, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            ContentDiagnostic diagnostic = result.Diagnostics[0];
            Assert.That(diagnostic.File, Is.EqualTo(Map));
            Assert.That(diagnostic.FieldPath, Is.EqualTo(expectedFieldPath));
            Assert.That(diagnostic.Message, Does.Contain(expectedMessagePart));
            Assert.That(diagnostic.Line, Is.GreaterThan(0));
        }
    }

    [Test]
    public void Run_WhenNavigationIsMissing_ReportsTheField()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            string text = workspace.Read(Map).Replace("\r\n", "\n");
            int start = text.IndexOf("navigation:\n", StringComparison.Ordinal);
            int end = text.IndexOf("client:\n", StringComparison.Ordinal);
            workspace.Write(Map, text.Remove(start, end - start));

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            Assert.That(result.Diagnostics[0].FieldPath, Is.EqualTo("navigation"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("required field is missing"));
        }
    }

    [Test]
    public void Run_WhenMonsterCenterIsOnThePlateau_IsValidBecauseTheRampReachesIt()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            workspace.Replace(Map, MonsterCenter, "center: { x: -13.0, y: 1.0, z: 13.0 }");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        }
    }

    [Test]
    public void Run_ForValidFixture_ReadsRowsNorthFirstIntoTheGrid()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            NavigationGrid grid = ContentPipeline.Run(workspace.ContentRoot).Content.Maps.Single().Definition
                .Navigation;

            Assert.That(grid.Columns, Is.EqualTo(40));
            Assert.That(grid.Rows, Is.EqualTo(40));
            Assert.That(grid.OriginX, Is.EqualTo(-20f));
            Assert.That(grid.AgentRadius, Is.EqualTo(0.3f));
            Assert.That(grid.MaxStepHeight, Is.EqualTo(0.4f));
            Assert.That(grid.GetCell(18, 16).Surface, Is.EqualTo(NavigationSurface.NpcMarker));
            Assert.That(grid.GetCell(39, 19).Surface, Is.EqualTo(NavigationSurface.Gate));
            Assert.That(grid.GetCell(6, 28), Is.EqualTo(NavigationCell.Ramp(RampAxis.Z, 0f, 0.5f)));
            Assert.That(grid.GetCell(6, 31), Is.EqualTo(NavigationCell.Level(NavigationSurface.Floor, 1f)));
        }
    }

    [Test]
    public void Build_ForMap_WritesIdenticalNavigationToBothPackages()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            ContentPackages packages = ContentPipeline.Run(workspace.ContentRoot).Packages!;

            string server = NavigationOf(packages.Server);
            string client = NavigationOf(packages.Client);

            Assert.That(client, Is.EqualTo(server));
            Assert.That(client, Does.Contain("\"cellRows\""));
        }
    }

    [Test]
    public void Build_ForMap_WritesCellRowsSouthFirstWithLetterSymbolsOnly()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);
            NavigationGrid grid = result.Content.Maps.Single().Definition.Navigation;

            using (JsonDocument document = JsonDocument.Parse(FileOf(result.Packages!.Client, "maps.json")))
            {
                JsonElement navigation = document.RootElement.GetProperty("definitions")[0].GetProperty("navigation");
                string[] cellRows = navigation.GetProperty("cellRows").EnumerateArray()
                    .Select(row => row.GetString()!)
                    .ToArray();
                Dictionary<string, string> surfaceBySymbol = navigation.GetProperty("legend").EnumerateArray()
                    .ToDictionary(
                        entry => entry.GetProperty("symbol").GetString()!,
                        entry => entry.GetProperty("surface").GetString()!);

                Assert.That(cellRows, Has.Length.EqualTo(grid.Rows));
                Assert.That(
                    cellRows,
                    Has.All.Matches<string>(row => row.Length == grid.Columns && row.All(char.IsLetter)));
                Assert.That(surfaceBySymbol[cellRows[16][18].ToString()], Is.EqualTo("npcMarker"));
                Assert.That(surfaceBySymbol[cellRows[0][0].ToString()], Is.EqualTo("wall"));
            }
        }
    }

    [Test]
    public void Run_ForRepositoryContent_TrainingGroundExercisesNavigation()
    {
        ContentPipelineResult result = ContentPipeline.Run(RepositoryContentDirectory());
        MapDefinition map = result.Content.Maps
            .Single(candidate => candidate.Definition.Id.Value == "map.training_ground")
            .Definition;
        NavigationGrid grid = map.Navigation;
        GridPathfinder pathfinder = new GridPathfinder(grid);
        List<WorldPosition> waypoints = new List<WorldPosition>();
        int budget = grid.Columns * grid.Rows;

        HashSet<NavigationSurface> surfaces = new HashSet<NavigationSurface>();
        bool hasRamp = false;
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                surfaces.Add(grid.GetCell(column, row).Surface);
                hasRamp |= grid.GetCell(column, row).Axis != RampAxis.None;
            }
        }

        Assert.That(surfaces, Is.EquivalentTo(Enum.GetValues(typeof(NavigationSurface))));
        Assert.That(hasRamp, Is.True);

        WorldPosition plateau = grid.GetCellCenter(9, 40);
        WorldPosition insideRoom = grid.GetCellCenter(8, 8);
        Assert.That(plateau.Y, Is.EqualTo(1f));
        Assert.That(pathfinder.TryFindPath(map.SpawnPosition, plateau, budget, waypoints), Is.True);
        Assert.That(waypoints.Count, Is.GreaterThan(1), "the plateau must need a detour over a ramp");
        Assert.That(pathfinder.TryFindPath(map.SpawnPosition, insideRoom, budget, waypoints), Is.True);
        Assert.That(waypoints.Count, Is.GreaterThan(1), "the room must need a detour through its door");
        Assert.That(grid.HasLineOfSight(map.SpawnPosition, map.MonsterSpawns[0].Center), Is.False);
    }

    private static string NavigationOf(ContentPackage package)
    {
        using (JsonDocument document = JsonDocument.Parse(FileOf(package, "maps.json")))
        {
            return document.RootElement.GetProperty("definitions")[0].GetProperty("navigation").GetRawText();
        }
    }

    private static byte[] FileOf(ContentPackage package, string path)
    {
        return package.DataFiles.Single(file => file.Path == path).Content;
    }

    private static string RepositoryContentDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Evertorch.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "content");
    }

    private static string Describe(ContentPipelineResult result)
    {
        StringBuilder text = new StringBuilder();
        foreach (ContentDiagnostic diagnostic in result.Diagnostics)
        {
            text.AppendLine(diagnostic.ToString());
        }

        return text.ToString();
    }
}
}
