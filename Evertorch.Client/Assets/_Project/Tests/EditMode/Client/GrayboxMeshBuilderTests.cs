using System.Linq;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class GrayboxMeshBuilderTests
{
    [TestCase(NavigationSurface.NpcMarker, GrayboxSurface.NpcMarker, GrayboxMeshBuilder.NpcMarkerHeight)]
    [TestCase(NavigationSurface.Gate, GrayboxSurface.Gate, GrayboxMeshBuilder.GateHeight)]
    [TestCase(NavigationSurface.Obstacle, GrayboxSurface.Obstacle, GrayboxMeshBuilder.ObstacleHeight)]
    public void Build_DrawsEachBlockingSurfaceInItsOwnColourAndHeight(
        NavigationSurface surface,
        GrayboxSurface expected,
        float height)
    {
        NavigationGrid grid = Create(1, NavigationCell.Level(surface, 0f));

        Mesh mesh = GrayboxMeshBuilder.Build(grid);

        Assert.That(mesh.GetTriangles((int)expected).Length, Is.EqualTo(5 * 2 * 3));
        Assert.That(mesh.bounds.max.y, Is.EqualTo(height).Within(1e-5f));
        Object.DestroyImmediate(mesh);
    }

    private static NavigationGrid Create(int columns, params NavigationCell[] cells)
    {
        return new NavigationGrid(columns, 1, 1f, 10f, 20f, 0.3f, 0.4f, cells);
    }

    [Test]
    public void Build_EveryFaceOfABlockPointsOutward()
    {
        NavigationGrid grid = Create(1, NavigationCell.Level(NavigationSurface.Obstacle, 0f));

        Mesh mesh = GrayboxMeshBuilder.Build(grid);

        var center = new Vector3(10.5f, GrayboxMeshBuilder.ObstacleHeight * 0.5f, 20.5f);
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.GetTriangles((int)GrayboxSurface.Obstacle);
        for (int index = 0; index < triangles.Length; index += 3)
        {
            Vector3 a = vertices[triangles[index]];
            Vector3 b = vertices[triangles[index + 1]];
            Vector3 c = vertices[triangles[index + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            Vector3 outward = (a + b + c) / 3f - center;
            Assert.That(Vector3.Dot(normal, outward), Is.GreaterThan(0f), $"triangle {index / 3}");
        }

        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void Build_FloorFacesUp()
    {
        NavigationGrid grid = Create(1, NavigationCell.Level(NavigationSurface.Floor, 0f));

        Mesh mesh = GrayboxMeshBuilder.Build(grid);

        Assert.That(mesh.normals.All(normal => normal.y > 0.99f), Is.True);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void Build_HasOneSubMeshPerSurfaceAndCoversTheGrid()
    {
        NavigationGrid grid = Create(
            2,
            NavigationCell.Level(NavigationSurface.Floor, 0f),
            NavigationCell.Level(NavigationSurface.Wall, 0f));

        Mesh mesh = GrayboxMeshBuilder.Build(grid);

        Assert.That(mesh.subMeshCount, Is.EqualTo(6));
        Assert.That(mesh.bounds.min.x, Is.EqualTo(10f).Within(1e-5f));
        Assert.That(mesh.bounds.max.x, Is.EqualTo(12f).Within(1e-5f));
        Assert.That(mesh.bounds.min.z, Is.EqualTo(20f).Within(1e-5f));
        Assert.That(mesh.bounds.max.z, Is.EqualTo(21f).Within(1e-5f));
        Assert.That(mesh.bounds.max.y, Is.EqualTo(GrayboxMeshBuilder.WallHeight).Within(1e-5f));
        Assert.That(mesh.GetTriangles((int)GrayboxSurface.Floor).Length, Is.EqualTo(2 * 2 * 3), "a floor under both");
        Assert.That(mesh.GetTriangles((int)GrayboxSurface.Wall).Length, Is.EqualTo(5 * 2 * 3), "a top and four sides");
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void Build_RampFollowsTheHeightsTheServerWalksOn()
    {
        NavigationGrid grid = Create(1, NavigationCell.Ramp(RampAxis.X, 0.1f, 0.3f));

        Mesh mesh = GrayboxMeshBuilder.Build(grid);

        Vector3[] top = mesh.vertices.Take(4).ToArray();
        foreach (Vector3 corner in top)
        {
            Assert.That(grid.TrySampleHeight(Mathf.Clamp(corner.x, 10.001f, 10.999f), 20.5f, out float height),
                Is.True);
            Assert.That(corner.y, Is.EqualTo(height).Within(1e-3f), $"corner at x {corner.x}");
        }

        Assert.That(mesh.GetTriangles((int)GrayboxSurface.RaisedFloor).Length, Is.GreaterThan(0));
        Assert.That(mesh.GetTriangles((int)GrayboxSurface.Floor).Length, Is.EqualTo(0));
        Object.DestroyImmediate(mesh);
    }

    // Each NPC stands on its marker, drawn as a low plinth instead of a pillar (Prototype Content §5).
    [Test]
    public void NpcMarker_IsALowPlinth()
    {
        Assert.That(GrayboxMeshBuilder.NpcMarkerHeight, Is.EqualTo(0.2f));
    }
}
}
