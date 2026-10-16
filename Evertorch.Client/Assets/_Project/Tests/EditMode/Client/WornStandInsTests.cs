using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The worn pieces' stand-ins (owner decision 12 of Milestone 20): each position's shapes on the shared skeleton's
///     bones, placed from the bind pose, in their item's colour, the cap under the outfit's overhead point.
/// </summary>
[TestFixture]
public sealed class WornStandInsTests
{
    [TearDown]
    public void DestroyCreated()
    {
        foreach (Object created in m_created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        m_created.Clear();
    }

    private readonly List<Object> m_created = new();

    // The shared humanoid skeleton's bones as delivered, in its bind pose: facing +Z, its left side +X, an arm's bones
    // pointing along it, a foot's toward its toes.
    private Transform Skeleton(float overheadHeight)
    {
        var body = new GameObject("Body");
        m_created.Add(body);
        Bone(body.transform, "Hips", new Vector3(0f, 0.85f, 0f), Vector3.forward, Vector3.up);
        Bone(body.transform, "Chest", new Vector3(0f, 1.09f, 0f), Vector3.forward, Vector3.up);
        Bone(body.transform, "UpperChest", new Vector3(0f, 1.18f, 0f), Vector3.forward, Vector3.up);
        Bone(body.transform, "Head", new Vector3(0f, 1.35f, 0f), Vector3.forward, Vector3.up);
        Bone(body.transform, "LeftLowerArm", new Vector3(0.435f, 1.2f, 0f), Vector3.up, Vector3.right);
        var footForward = new Vector3(0f, -0.88f, -0.48f);
        var footUp = new Vector3(0f, -0.48f, 0.88f);
        Bone(body.transform, "LeftFoot", new Vector3(0.118f, 0.135f, 0f), footForward, footUp);
        Bone(body.transform, "RightFoot", new Vector3(-0.118f, 0.135f, 0f), footForward, footUp);
        Bone(body.transform, "Anchor_Overhead", new Vector3(0f, overheadHeight, 0f), Vector3.forward, Vector3.up);
        return body.transform;
    }

    private static void Bone(Transform body, string name, Vector3 position, Vector3 forward, Vector3 up)
    {
        Transform bone = new GameObject(name).transform;
        bone.SetParent(body, false);
        bone.localPosition = position;
        bone.localRotation = Quaternion.LookRotation(forward, up);
    }

    private static Transform Find(Transform body, string name)
    {
        return body.GetComponentsInChildren<Transform>().Single(part => part.name == name);
    }

    private static readonly EquipmentSlot[] Worn =
    {
        EquipmentSlot.Armor, EquipmentSlot.Headgear, EquipmentSlot.Shield, EquipmentSlot.Garment,
        EquipmentSlot.Footwear, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2
    };

    [Test]
    public void ColourOf_AnItem_IsAlwaysTheSame_AndTheTenPiecesTakeSeveral()
    {
        string[] pieces =
        {
            "item.headgear.trail_cap", "item.headgear.grotto_circlet", "item.shield.wooden_buckler",
            "item.shield.carapace", "item.garment.travel_cape", "item.garment.gloom_cape",
            "item.footwear.trail_boots", "item.footwear.grotto_boots", "item.accessory.traveler_charm",
            "item.accessory.grotto_charm"
        };

        Color[] colours = pieces.Select(piece => WornStandIns.ColourOf(new ItemDefinitionId(piece))).ToArray();
        Color[] again = pieces.Select(piece => WornStandIns.ColourOf(new ItemDefinitionId(piece))).ToArray();

        Assert.That(again, Is.EqualTo(colours));
        Assert.That(colours.Distinct().Count(), Is.GreaterThanOrEqualTo(4), "a town piece and a grotto piece differ");
        Assert.That(colours.Select(colour => colour.a), Is.All.EqualTo(1f));
    }

    // Each shape is closed toward the outside, so its faces show from wherever the camera stands, and stays cheap: a
    // full loadout of twelve parts keeps within the pieces' budget (the plan's budgets).
    [Test]
    public void CreateMesh_OfEachShape_FacesOutward_InFewTriangles()
    {
        var centres = new Dictionary<StandInShape, Vector3>
        {
            [StandInShape.Dome] = new(0f, 0.3f, 0f),
            [StandInShape.Disc] = new(0f, 0.5f, 0f),
            [StandInShape.Tube] = new(0f, 0.5f, 0f),
            [StandInShape.Box] = Vector3.zero,
            [StandInShape.Gem] = Vector3.zero
        };

        foreach (KeyValuePair<StandInShape, Vector3> shape in centres)
        {
            Mesh mesh = WornStandIns.CreateMesh(shape.Key);
            m_created.Add(mesh);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 a = vertices[triangles[index]];
                Vector3 b = vertices[triangles[index + 1]];
                Vector3 c = vertices[triangles[index + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                Assert.That(
                    Vector3.Dot(normal, (a + b + c) / 3f - shape.Value),
                    Is.GreaterThan(0f),
                    $"{shape.Key}, triangle {index / 3}");
            }

            Assert.That(triangles.Length / 3, Is.InRange(8, 128), shape.Key.ToString());
        }
    }

    [Test]
    public void PartsOf_EachPosition_NamesItsShapesAndBones_TheWeaponNone()
    {
        string Describe(EquipmentSlot position)
        {
            return string.Join(
                ", ",
                WornStandIns.PartsOf(position, 1.56f)
                    .Select(part => $"{part.Shape} {part.Bone}{(part.IsTrim ? " trim" : string.Empty)}"));
        }

        Assert.That(Describe(EquipmentSlot.Weapon), Is.Empty, "the weapon is held");
        Assert.That(Describe(EquipmentSlot.Armor), Is.EqualTo("Tube Chest, Tube Chest trim"));
        Assert.That(Describe(EquipmentSlot.Headgear), Is.EqualTo("Dome Head, Disc Head"));
        Assert.That(Describe(EquipmentSlot.Shield), Is.EqualTo("Disc LeftLowerArm, Disc LeftLowerArm trim"));
        Assert.That(Describe(EquipmentSlot.Garment), Is.EqualTo("Box UpperChest, Box UpperChest trim"));
        Assert.That(Describe(EquipmentSlot.Footwear), Is.EqualTo("Box LeftFoot, Box RightFoot"));
        Assert.That(Describe(EquipmentSlot.Accessory1), Is.EqualTo("Gem Hips"));
        Assert.That(Describe(EquipmentSlot.Accessory2), Is.EqualTo("Gem Hips"));
        Assert.That(
            Worn.Sum(position => WornStandIns.PartsOf(position, 1.56f).Count),
            Is.LessThanOrEqualTo(12),
            "a full loadout's renderers");
    }

    // Each part hangs from its bone where the bind pose puts it, whatever the bone's own axes, so it follows the bone
    // through the clips; the cap stays under the outfit's overhead point, and accessory 1 is on the left.
    [Test]
    public void Place_OnTheSharedSkeleton_PutsEachPartWhereTheBindPoseDoes()
    {
        Transform body = Skeleton(1.97f);
        Transform overhead = Find(body, "Anchor_Overhead");

        foreach (EquipmentSlot position in Worn)
        {
            IReadOnlyList<StandInPart> parts = WornStandIns.PartsOf(position, 1.56f);
            IReadOnlyList<StandInPlacement> placements = WornStandIns.Place(body, position, overhead);
            Assert.That(placements.Count, Is.EqualTo(parts.Count), position.ToString());
            for (int index = 0; index < parts.Count; index++)
            {
                StandInPlacement placement = placements[index];
                Vector3 origin = body.InverseTransformPoint(placement.Bone.TransformPoint(placement.LocalPosition));
                Quaternion rotation = Quaternion.Inverse(body.rotation) * placement.Bone.rotation
                    * placement.LocalRotation;
                Assert.That(placement.Bone.name, Is.EqualTo(parts[index].Bone));
                Assert.That(Vector3.Distance(origin, parts[index].Origin), Is.LessThan(0.0001f), $"{position} {index}");
                Assert.That(
                    Quaternion.Angle(rotation, parts[index].Rotation),
                    Is.LessThan(0.01f),
                    $"{position} {index}");
                Assert.That(placement.LocalScale, Is.EqualTo(parts[index].Size), $"{position} {index}");
            }
        }

        StandInPlacement left = WornStandIns.Place(body, EquipmentSlot.Accessory1, overhead)[0];
        StandInPlacement right = WornStandIns.Place(body, EquipmentSlot.Accessory2, overhead)[0];
        Assert.That(left.LocalPosition.x, Is.GreaterThan(0f), "accessory 1 on the left hip");
        Assert.That(right.LocalPosition.x, Is.LessThan(0f), "accessory 2 on the right");
    }

    // A hood lifts the overhead point, and the cap with it; a body without the point keeps the Adventurer's height.
    [Test]
    public void Place_TheCap_StaysUnderTheOverheadPoint()
    {
        Transform hooded = Skeleton(2.04f);
        Transform plain = Skeleton(1.97f);

        float Cap(Transform body, Transform? overhead)
        {
            StandInPlacement dome = WornStandIns.Place(body, EquipmentSlot.Headgear, overhead)[0];
            return body.InverseTransformPoint(dome.Bone.TransformPoint(dome.LocalPosition)).y;
        }

        Assert.That(Cap(hooded, Find(hooded, "Anchor_Overhead")), Is.EqualTo(1.63f).Within(0.0001f));
        Assert.That(Cap(plain, Find(plain, "Anchor_Overhead")), Is.EqualTo(1.56f).Within(0.0001f));
        Assert.That(Cap(plain, null), Is.EqualTo(1.56f).Within(0.0001f));
    }

    // A body without a bone a position needs, such as a placeholder, wears nothing there.
    [Test]
    public void Place_WithoutItsBone_PlacesNothing()
    {
        var body = new GameObject("Placeholder");
        m_created.Add(body);
        Bone(body.transform, "Chest", new Vector3(0f, 1.09f, 0f), Vector3.forward, Vector3.up);

        Assert.That(WornStandIns.Place(body.transform, EquipmentSlot.Armor, null).Count, Is.EqualTo(2));
        Assert.That(WornStandIns.Place(body.transform, EquipmentSlot.Footwear, null), Is.Empty);
        Assert.That(WornStandIns.Place(body.transform, EquipmentSlot.Weapon, null), Is.Empty);
    }
}
}
