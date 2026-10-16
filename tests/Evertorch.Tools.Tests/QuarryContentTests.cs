using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     The Ashen Quarry as content (Gameplay Systems §4.2; Prototype Content §6): the open quarry beyond the training
///     field's northern gate. Each later line of Milestone 21 adds what it places in the quarry.
/// </summary>
[TestFixture]
public sealed class QuarryContentTests
{
    private const string Quarry = "map.ashen_quarry";
    private const string Field = "map.training_field";

    // The path budget of the client's movement and of the monsters: a map of at most this many cells can always be
    // crossed in one path.
    private const int PathNodeBudget = 8192;

    private static ContentPipelineResult RepositoryContent()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(
            result.Diagnostics,
            Is.Empty,
            string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return result;
    }

    private static MapDefinition MapOf(ContentPipelineResult result, string id)
    {
        return result.Content.Maps.Single(map => map.Definition.Id.Value == id).Definition;
    }

    // The x of each gate cell along the row of cells at z, west to east.
    private static float[] GatesAlong(NavigationGrid grid, float z)
    {
        var gates = new List<float>();
        for (int column = 0; column < grid.Columns; column++)
        {
            float x = grid.GetCellCenter(column, 0).X;
            if (grid.TryGetCellIndex(x, z, out int found, out int row)
                && grid.GetCell(found, row).Surface == NavigationSurface.Gate)
            {
                gates.Add(x);
            }
        }

        return gates.ToArray();
    }

    // The arrival yard in the south, the crossroads, the cinder basin in the west, the basalt galleries' three alcoves
    // in the east, the ash terraces in the north-west and north-east, and the furnace arena between them.
    [TestCase(0f, -38.5f, "the portal before the south gate")]
    [TestCase(0f, -5f, "the crossroads")]
    [TestCase(-28f, -10f, "the cinder basin")]
    [TestCase(32f, -27f, "the basalt galleries' southern alcove")]
    [TestCase(32f, -6f, "their middle alcove")]
    [TestCase(32f, 12f, "their northern alcove")]
    [TestCase(-27f, 28f, "the western ash terrace")]
    [TestCase(27f, 28f, "the eastern ash terrace")]
    [TestCase(0f, 28f, "the furnace arena")]
    public void Run_ForRepositoryContent_LetsEveryPartOfTheQuarryBeWalkedToFromItsSpawnPoint(
        float x,
        float z,
        string part)
    {
        MapDefinition quarry = MapOf(RepositoryContent(), Quarry);
        NavigationGrid grid = quarry.Navigation;
        var waypoints = new List<WorldPosition>();

        bool isReachable = new GridPathfinder(grid).TryFindPath(
            quarry.SpawnPosition,
            new WorldPosition(x, 0f, z),
            grid.Columns * grid.Rows,
            waypoints);

        Assert.That((grid.Columns, grid.Rows, grid.CellSize), Is.EqualTo((80, 80, 1f)));
        Assert.That(grid.Columns * grid.Rows, Is.LessThanOrEqualTo(PathNodeBudget));
        Assert.That(isReachable, Is.True, part);
    }

    // Owner decision 20: two gate cells in the middle of the field's north wall and two in the quarry's south wall,
    // each with its portal on the floor before it; an arrival stands two metres or one and a half inside the gate,
    // facing away from it.
    [Test]
    public void Run_ForRepositoryContent_JoinsTheFieldsNorthGateToTheQuarrysSouthGate()
    {
        ContentPipelineResult result = RepositoryContent();
        MapDefinition field = MapOf(result, Field);
        MapDefinition quarry = MapOf(result, Quarry);
        MapPortal north = field.Portals.Single(portal => portal.DestinationMap == quarry.Id);
        MapPortal south = quarry.Portals.Single();

        Assert.That(GatesAlong(field.Navigation, 23.5f), Is.EqualTo(new[] { -12.5f, -11.5f }),
            "the field's north gate");
        Assert.That(GatesAlong(quarry.Navigation, -39.5f), Is.EqualTo(new[] { -0.5f, 0.5f }),
            "the quarry's south gate");
        Assert.That(
            (north.Center.X, north.Center.Z, north.Radius, north.DestinationPosition.X, north.DestinationPosition.Z,
                north.DestinationFacing.Z),
            Is.EqualTo((-12f, 22.5f, 1d, 0f, -37f, 1f)));
        Assert.That(
            (south.DestinationMap.Value, south.Center.X, south.Center.Z, south.Radius, south.DestinationPosition.X,
                south.DestinationPosition.Z, south.DestinationFacing.Z),
            Is.EqualTo((Field, 0f, -38.5f, 1d, -12f, 20.5f, -1f)));
        Assert.That(quarry.SpawnPosition, Is.EqualTo(north.DestinationPosition), "arrivals in the yard");
        Assert.That(
            field.Portals.Select(portal => portal.DestinationMap.Value),
            Is.EqualTo(new[] { "map.training_ground", "map.umbral_grotto", Quarry }),
            "after the field's two portals, which keep their places");
    }
}
}
