using System;
using System.Collections.Generic;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     The stand-ins of the worn pieces until their art is delivered (owner decision 12 of Milestone 20): a simple shape
///     per position in its item's colour, rigid on a bone of the shared humanoid skeleton. A cap sits on the head under
///     the outfit's overhead point, a shell around the chest, a disc on the left forearm, a cape at the back, a boot on
///     each foot, and a charm at each hip, accessory 1 on the left. Body armor, a garment, and a shield carry a Trim band
///     in the player's colour. Each part is placed from the body's bind pose, so it follows the clips with its bone.
/// </summary>
public static class WornStandIns
{
    // The cap's base under the outfit's overhead point: 1.97 m over 1.56 m on the Adventurer, so a hood lifts the cap.
    private const float CapUnderOverhead = 0.41f;

    // The cap's base on a body without an overhead point.
    private const float DefaultCapBase = 1.56f;

    // The cape hangs out from the back by this angle, clear of the legs in a stride.
    private const float CapeTilt = 8f;

    // Muted colours that read against the outfits at the game's camera; an item takes one by a hash of its ID.
    private static readonly Color[] Palette =
    {
        new(0.55f, 0.38f, 0.24f), new(0.55f, 0.57f, 0.6f), new(0.36f, 0.48f, 0.28f), new(0.6f, 0.22f, 0.2f),
        new(0.22f, 0.3f, 0.52f), new(0.82f, 0.78f, 0.66f), new(0.7f, 0.5f, 0.22f), new(0.24f, 0.48f, 0.5f)
    };

    /// <summary>
    ///     The colour of a Trim band on a body without a player's tint.
    /// </summary>
    public static readonly Color TrimColor = new(0.82f, 0.78f, 0.66f);

    /// <summary>
    ///     The stand-in colour of <paramref name="item" />, the same on every body.
    /// </summary>
    public static Color ColourOf(ItemDefinitionId item)
    {
        // FNV-1a, so the colour never depends on the runtime's string hash.
        uint hash = 2166136261;
        foreach (char character in item.Value)
        {
            hash = (hash ^ character) * 16777619;
        }

        return Palette[hash % (uint)Palette.Length];
    }

    /// <summary>
    ///     The parts of what <paramref name="position" /> holds, in the bind-pose space of a body facing +Z whose left side
    ///     is +X, the cap's base at <paramref name="capBase" />; none for the weapon, which is held.
    /// </summary>
    public static IReadOnlyList<StandInPart> PartsOf(EquipmentSlot position, float capBase)
    {
        Quaternion level = Quaternion.identity;
        var cape = Quaternion.Euler(CapeTilt, 0f, 0f);
        Vector3 capeDown = cape * Vector3.down;
        var capeCentre = new Vector3(0f, 1.075f, -0.235f);
        return position switch
        {
            EquipmentSlot.Armor => new[]
            {
                new StandInPart(StandInShape.Tube, "Chest", new Vector3(0f, 1.03f, 0f), level,
                    new Vector3(0.235f, 0.28f, 0.2f), false),
                new StandInPart(StandInShape.Tube, "Chest", new Vector3(0f, 1.135f, 0f), level,
                    new Vector3(0.245f, 0.04f, 0.21f), true)
            },
            EquipmentSlot.Headgear => new[]
            {
                new StandInPart(StandInShape.Dome, "Head", new Vector3(0f, capBase, 0.005f), level,
                    new Vector3(0.205f, 0.17f, 0.195f), false),
                new StandInPart(StandInShape.Disc, "Head", new Vector3(0f, capBase - 0.012f, 0.02f), level,
                    new Vector3(0.245f, 0.022f, 0.245f), false)
            },
            EquipmentSlot.Shield => new[]
            {
                new StandInPart(StandInShape.Disc, "LeftLowerArm", new Vector3(0.54f, 1.29f, 0f), level,
                    new Vector3(0.2f, 0.035f, 0.2f), false),
                new StandInPart(StandInShape.Disc, "LeftLowerArm", new Vector3(0.54f, 1.32f, 0f), level,
                    new Vector3(0.06f, 0.03f, 0.06f), true)
            },
            EquipmentSlot.Garment => new[]
            {
                new StandInPart(
                    StandInShape.Box,
                    "UpperChest",
                    capeCentre,
                    cape,
                    new Vector3(0.42f, 0.44f, 0.02f),
                    false),
                new StandInPart(StandInShape.Box, "UpperChest", capeCentre + capeDown * 0.2f, cape,
                    new Vector3(0.424f, 0.035f, 0.026f), true)
            },
            EquipmentSlot.Footwear => new[]
            {
                new StandInPart(StandInShape.Box, "LeftFoot", new Vector3(0.118f, 0.105f, 0.072f), level,
                    new Vector3(0.215f, 0.21f, 0.35f), false),
                new StandInPart(StandInShape.Box, "RightFoot", new Vector3(-0.118f, 0.105f, 0.072f), level,
                    new Vector3(0.215f, 0.21f, 0.35f), false)
            },
            EquipmentSlot.Accessory1 => new[]
            {
                new StandInPart(StandInShape.Gem, "Hips", new Vector3(0.33f, 0.95f, 0.11f),
                    Quaternion.Euler(0f, 45f, 0f), Vector3.one * 0.06f, false)
            },
            EquipmentSlot.Accessory2 => new[]
            {
                new StandInPart(StandInShape.Gem, "Hips", new Vector3(-0.33f, 0.95f, 0.11f),
                    Quaternion.Euler(0f, 45f, 0f), Vector3.one * 0.06f, false)
            },
            _ => Array.Empty<StandInPart>()
        };
    }

    /// <summary>
    ///     Where each part of <paramref name="position" /> hangs on <paramref name="body" />, read while the body stands in
    ///     its bind pose, as a body does until its clips first play; empty when a bone it needs is missing, as on a
    ///     placeholder. <paramref name="overhead" /> is the outfit's overhead point, which the cap stays under.
    /// </summary>
    public static IReadOnlyList<StandInPlacement> Place(Transform body, EquipmentSlot position, Transform? overhead)
    {
        if (body == null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        float capBase = overhead != null
            ? body.InverseTransformPoint(overhead.position).y - CapUnderOverhead
            : DefaultCapBase;
        IReadOnlyList<StandInPart> parts = PartsOf(position, capBase);
        var placements = new StandInPlacement[parts.Count];
        for (int index = 0; index < parts.Count; index++)
        {
            StandInPart part = parts[index];
            Transform? bone = FindBone(body, part.Bone);
            if (bone == null)
            {
                return Array.Empty<StandInPlacement>();
            }

            float scale = body.lossyScale.x / bone.lossyScale.x;
            placements[index] = new StandInPlacement(
                bone,
                bone.InverseTransformPoint(body.TransformPoint(part.Origin)),
                Quaternion.Inverse(bone.rotation) * body.rotation * part.Rotation,
                part.Size * scale,
                part.Shape,
                part.IsTrim);
        }

        return placements;
    }

    /// <summary>
    ///     The unit mesh of <paramref name="shape" />: a dome, a disc, and a tube stand on their base at the origin and
    ///     reach 1 up, with radius 1; a box and a gem are centred, a box 1 on a side and a gem 1 to each point.
    /// </summary>
    public static Mesh CreateMesh(StandInShape shape)
    {
        var builder = new FlatMesh();
        switch (shape)
        {
            case StandInShape.Dome:
                BuildDome(builder);
                break;
            case StandInShape.Disc:
                BuildCylinder(builder, true, 0f);
                break;
            case StandInShape.Tube:
                BuildCylinder(builder, false, 0.45f);
                break;
            case StandInShape.Box:
                BuildBox(builder);
                break;
            case StandInShape.Gem:
                BuildGem(builder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }

        return builder.ToMesh($"StandIn {shape}");
    }

    private static Transform? FindBone(Transform body, string name)
    {
        foreach (Transform part in body.GetComponentsInChildren<Transform>(true))
        {
            if (part.name == name)
            {
                return part;
            }
        }

        return null;
    }

    private static Vector3 Around(float angle, float radius, float height)
    {
        return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
    }

    private static void BuildDome(FlatMesh builder)
    {
        const int segments = 12;
        const int rings = 4;
        for (int segment = 0; segment < segments; segment++)
        {
            float from = 2f * Mathf.PI * segment / segments;
            float to = 2f * Mathf.PI * (segment + 1) / segments;
            for (int ring = 0; ring < rings; ring++)
            {
                float low = 0.5f * Mathf.PI * ring / rings;
                float high = 0.5f * Mathf.PI * (ring + 1) / rings;
                Vector3 a = Around(from, Mathf.Cos(low), Mathf.Sin(low));
                Vector3 b = Around(from, Mathf.Cos(high), Mathf.Sin(high));
                Vector3 c = Around(to, Mathf.Cos(high), Mathf.Sin(high));
                Vector3 d = Around(to, Mathf.Cos(low), Mathf.Sin(low));
                builder.AddQuad(a, b, c, d, (a + c) * 0.5f);
            }

            builder.AddTriangle(Vector3.zero, Around(from, 1f, 0f), Around(to, 1f, 0f), Vector3.down);
        }
    }

    // A disc is closed at both ends; a tube is open below and closed above by a ring around a hole of the inner radius.
    private static void BuildCylinder(FlatMesh builder, bool isClosed, float innerRadius)
    {
        const int segments = 16;
        for (int segment = 0; segment < segments; segment++)
        {
            float from = 2f * Mathf.PI * segment / segments;
            float to = 2f * Mathf.PI * (segment + 1) / segments;
            Vector3 a = Around(from, 1f, 0f);
            Vector3 b = Around(from, 1f, 1f);
            Vector3 c = Around(to, 1f, 1f);
            Vector3 d = Around(to, 1f, 0f);
            builder.AddQuad(a, b, c, d, Around((from + to) * 0.5f, 1f, 0f));
            if (isClosed)
            {
                builder.AddTriangle(Vector3.up, b, c, Vector3.up);
                builder.AddTriangle(Vector3.zero, a, d, Vector3.down);
            }
            else
            {
                builder.AddQuad(Around(from, innerRadius, 1f), b, c, Around(to, innerRadius, 1f), Vector3.up);
            }
        }
    }

    private static void BuildBox(FlatMesh builder)
    {
        foreach (Vector3 normal in new[]
                 {
                     Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
                 })
        {
            Vector3 u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
            var v = Vector3.Cross(normal, u);
            Vector3 centre = normal * 0.5f;
            builder.AddQuad(
                centre - (u + v) * 0.5f,
                centre - (u - v) * 0.5f,
                centre + (u + v) * 0.5f,
                centre + (u - v) * 0.5f,
                normal);
        }
    }

    private static void BuildGem(FlatMesh builder)
    {
        Vector3[] around = { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };
        for (int index = 0; index < around.Length; index++)
        {
            Vector3 a = around[index];
            Vector3 b = around[(index + 1) % around.Length];
            builder.AddTriangle(Vector3.up, a, b, (a + b + Vector3.up) / 3f);
            builder.AddTriangle(Vector3.down, a, b, (a + b + Vector3.down) / 3f);
        }
    }

    // Each triangle has vertices of its own, so the shapes read faceted, and is wound to face its outward direction.
    private sealed class FlatMesh
    {
        private readonly List<Vector3> m_vertices = new();

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f)
            {
                return;
            }

            m_vertices.Add(a);
            if (Vector3.Dot(normal, outward) >= 0f)
            {
                m_vertices.Add(b);
                m_vertices.Add(c);
            }
            else
            {
                m_vertices.Add(c);
                m_vertices.Add(b);
            }
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            AddTriangle(a, b, c, outward);
            AddTriangle(a, c, d, outward);
        }

        public Mesh ToMesh(string name)
        {
            int[] triangles = new int[m_vertices.Count];
            for (int index = 0; index < triangles.Length; index++)
            {
                triangles[index] = index;
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(m_vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

/// <summary>
///     The shapes the stand-ins are made of.
/// </summary>
public enum StandInShape
{
    Dome,
    Disc,
    Tube,
    Box,
    Gem
}

/// <summary>
///     One part of a stand-in in a body's bind-pose space: its shape, the bone it hangs from, where its origin stands,
///     how it turns, and its size; a Trim part takes the player's colour.
/// </summary>
public readonly struct StandInPart
{
    public StandInPart(StandInShape shape, string bone, Vector3 origin, Quaternion rotation, Vector3 size, bool isTrim)
    {
        Shape = shape;
        Bone = bone;
        Origin = origin;
        Rotation = rotation;
        Size = size;
        IsTrim = isTrim;
    }

    public StandInShape Shape { get; }

    public string Bone { get; }

    public Vector3 Origin { get; }

    public Quaternion Rotation { get; }

    public Vector3 Size { get; }

    public bool IsTrim { get; }
}

/// <summary>
///     One part of a stand-in placed on a body: its bone, and its pose and size in the bone's space.
/// </summary>
public readonly struct StandInPlacement
{
    public StandInPlacement(
        Transform bone,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        StandInShape shape,
        bool isTrim)
    {
        Bone = bone;
        LocalPosition = localPosition;
        LocalRotation = localRotation;
        LocalScale = localScale;
        Shape = shape;
        IsTrim = isTrim;
    }

    public Transform Bone { get; }

    public Vector3 LocalPosition { get; }

    public Quaternion LocalRotation { get; }

    public Vector3 LocalScale { get; }

    public StandInShape Shape { get; }

    public bool IsTrim { get; }
}
}
