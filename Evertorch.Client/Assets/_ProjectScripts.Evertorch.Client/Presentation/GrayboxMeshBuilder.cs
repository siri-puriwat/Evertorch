using System;
using System.Collections.Generic;
using Evertorch.Game;
using UnityEngine;
using UnityEngine.Rendering;

namespace Evertorch.Client
{
/// <summary>
///     Turns a navigation grid into a graybox mesh, one submesh per <see cref="GrayboxSurface" />. For the graybox slice
///     the grid is the map geometry, so what is drawn is exactly what the server collides with.
/// </summary>
public static class GrayboxMeshBuilder
{
    public const float WallHeight = 2f;
    public const float ObstacleHeight = 1f;
    public const float NpcMarkerHeight = 0.2f;
    public const float GateHeight = 2.6f;

    private const float NpcMarkerInset = 0.3f;
    private const float LevelTolerance = 0.001f;

    public static int SubMeshCount => Enum.GetValues(typeof(GrayboxSurface)).Length;

    public static Mesh Build(NavigationGrid grid)
    {
        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        var vertices = new List<Vector3>();
        var triangles = new List<int>[SubMeshCount];
        for (int index = 0; index < triangles.Length; index++)
        {
            triangles[index] = new List<int>();
        }

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                AddCell(grid, column, row, vertices, triangles);
            }
        }

        var mesh = new Mesh
        {
            name = "Graybox",
            indexFormat = IndexFormat.UInt32,
            subMeshCount = SubMeshCount
        };
        mesh.SetVertices(vertices);
        for (int index = 0; index < triangles.Length; index++)
        {
            mesh.SetTriangles(triangles[index], index);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddCell(
        NavigationGrid grid,
        int column,
        int row,
        List<Vector3> vertices,
        List<int>[] triangles)
    {
        NavigationCell cell = grid.GetCell(column, row);
        float x0 = grid.OriginX + column * grid.CellSize;
        float z0 = grid.OriginZ + row * grid.CellSize;
        float x1 = x0 + grid.CellSize;
        float z1 = z0 + grid.CellSize;

        // Corner heights in the order south-west, north-west, north-east, south-east.
        float southWest = cell.HeightAtMin;
        float northWest = cell.Axis == RampAxis.Z ? cell.HeightAtMax : cell.HeightAtMin;
        float northEast = cell.Axis == RampAxis.None ? cell.HeightAtMin : cell.HeightAtMax;
        float southEast = cell.Axis == RampAxis.X ? cell.HeightAtMax : cell.HeightAtMin;

        bool isRaised = Mathf.Max(Mathf.Max(southWest, northWest), Mathf.Max(northEast, southEast)) > LevelTolerance;
        List<int> ground = triangles[(int)(isRaised ? GrayboxSurface.RaisedFloor : GrayboxSurface.Floor)];
        AddQuad(
            vertices,
            ground,
            new Vector3(x0, southWest, z0),
            new Vector3(x0, northWest, z1),
            new Vector3(x1, northEast, z1),
            new Vector3(x1, southEast, z0));
        if (isRaised)
        {
            AddSides(vertices, ground, x0, z0, x1, z1, 0f, southWest, northWest, northEast, southEast);
        }

        if (cell.IsWalkable)
        {
            return;
        }

        float inset = cell.Surface == NavigationSurface.NpcMarker ? NpcMarkerInset * grid.CellSize : 0f;
        float top = cell.HeightAtMin + BlockHeight(cell.Surface);
        List<int> block = triangles[(int)ToGraybox(cell.Surface)];
        x0 += inset;
        z0 += inset;
        x1 -= inset;
        z1 -= inset;
        AddQuad(
            vertices,
            block,
            new Vector3(x0, top, z0),
            new Vector3(x0, top, z1),
            new Vector3(x1, top, z1),
            new Vector3(x1, top, z0));
        AddSides(vertices, block, x0, z0, x1, z1, cell.HeightAtMin, top, top, top, top);
    }

    private static void AddSides(
        List<Vector3> vertices,
        List<int> triangles,
        float x0,
        float z0,
        float x1,
        float z1,
        float bottom,
        float southWest,
        float northWest,
        float northEast,
        float southEast)
    {
        // Each face is listed clockwise as seen from outside the block.
        AddQuad(
            vertices,
            triangles,
            new Vector3(x0, bottom, z0),
            new Vector3(x0, southWest, z0),
            new Vector3(x1, southEast, z0),
            new Vector3(x1, bottom, z0));
        AddQuad(
            vertices,
            triangles,
            new Vector3(x1, bottom, z1),
            new Vector3(x1, northEast, z1),
            new Vector3(x0, northWest, z1),
            new Vector3(x0, bottom, z1));
        AddQuad(
            vertices,
            triangles,
            new Vector3(x0, bottom, z1),
            new Vector3(x0, northWest, z1),
            new Vector3(x0, southWest, z0),
            new Vector3(x0, bottom, z0));
        AddQuad(
            vertices,
            triangles,
            new Vector3(x1, bottom, z0),
            new Vector3(x1, southEast, z0),
            new Vector3(x1, northEast, z1),
            new Vector3(x1, bottom, z1));
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int first = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        triangles.Add(first);
        triangles.Add(first + 1);
        triangles.Add(first + 2);
        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 3);
    }

    private static float BlockHeight(NavigationSurface surface)
    {
        switch (surface)
        {
            case NavigationSurface.Obstacle:
                return ObstacleHeight;
            case NavigationSurface.NpcMarker:
                return NpcMarkerHeight;
            case NavigationSurface.Gate:
                return GateHeight;
            default:
                return WallHeight;
        }
    }

    private static GrayboxSurface ToGraybox(NavigationSurface surface)
    {
        switch (surface)
        {
            case NavigationSurface.Obstacle:
                return GrayboxSurface.Obstacle;
            case NavigationSurface.NpcMarker:
                return GrayboxSurface.NpcMarker;
            case NavigationSurface.Gate:
                return GrayboxSurface.Gate;
            default:
                return GrayboxSurface.Wall;
        }
    }
}
}
