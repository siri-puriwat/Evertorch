using System;
using System.Collections.Generic;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     The drawn and clickable form of one navigation grid. It owns the mesh and materials it creates.
/// </summary>
public sealed class GrayboxMap : MonoBehaviour
{
    private static readonly Color[] Palette =
    {
        new(0.42f, 0.47f, 0.40f),
        new(0.55f, 0.52f, 0.42f),
        new(0.30f, 0.31f, 0.34f),
        new(0.50f, 0.36f, 0.26f),
        new(0.85f, 0.72f, 0.25f),
        new(0.25f, 0.45f, 0.70f)
    };

    private Mesh? m_mesh;
    private Material[]? m_materials;

    public Collider? GroundCollider { get; private set; }

    private void OnDestroy()
    {
        if (m_mesh != null)
        {
            Destroy(m_mesh);
        }

        if (m_materials != null)
        {
            foreach (Material material in m_materials)
            {
                Destroy(material);
            }
        }
    }

    /// <summary>
    ///     Builds the map's mesh and collider, each surface coloured from <paramref name="palette" /> (a scene's
    ///     <see cref="MapLook" />), in <see cref="GrayboxSurface" /> order; a surface the palette has no colour for keeps
    ///     the default one.
    /// </summary>
    public static GrayboxMap Create(NavigationGrid grid, Material baseMaterial, IReadOnlyList<Color>? palette = null)
    {
        if (baseMaterial == null)
        {
            throw new ArgumentNullException(nameof(baseMaterial));
        }

        var root = new GameObject("GrayboxMap");
        GrayboxMap map = root.AddComponent<GrayboxMap>();
        map.m_mesh = GrayboxMeshBuilder.Build(grid);
        map.m_materials = new Material[GrayboxMeshBuilder.SubMeshCount];
        for (int index = 0; index < map.m_materials.Length; index++)
        {
            Color color = palette != null && index < palette.Count ? palette[index] : Palette[index];
            map.m_materials[index] = new Material(baseMaterial) { color = color };
        }

        root.AddComponent<MeshFilter>().sharedMesh = map.m_mesh;
        root.AddComponent<MeshRenderer>().sharedMaterials = map.m_materials;
        MeshCollider meshCollider = root.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = map.m_mesh;
        map.GroundCollider = meshCollider;
        return map;
    }
}
}
