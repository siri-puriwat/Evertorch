using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A delivered body wearing its pieces as stand-ins (owner decision 12 of Milestone 20): each position's parts hang
///     from their bones in the item's colour, the Trim parts in the player's, without a collider; they go with the
///     piece, take an ailment's tint as the body does, and a placeholder wears nothing.
/// </summary>
public sealed class WornStandInTests
{
    // Real time, not frames: in batch mode frames are not throttled.
    private const float TimeoutSeconds = 10f;

    private const float ColourTolerance = 0.002f;

    private static readonly Color Tint = new(0.2f, 0.45f, 1f);

    private static readonly ItemDefinitionId[] FullLoadout =
    {
        new("item.weapon.training_sword"), new("item.armor.leather"), new("item.headgear.trail_cap"),
        new("item.shield.wooden_buckler"), new("item.garment.travel_cape"), new("item.footwear.trail_boots"),
        new("item.accessory.traveler_charm"), new("item.accessory.grotto_charm")
    };

    private EntityViewCatalog? m_catalog;
    private EntityView? m_view;

    [TearDown]
    public void TearDown()
    {
        if (m_view != null)
        {
            Object.DestroyImmediate(m_view.gameObject);
        }

        m_catalog?.Dispose();
    }

    private static IEnumerator WaitForBody(EntityView view)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (!view.HasBody && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    // A colour read back from a material or a block may differ in its last bits from the one written.
    private static float Difference(Color a, Color b)
    {
        return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));
    }

    private static Color BlockColour(Renderer renderer, int? materialIndex = null)
    {
        var block = new MaterialPropertyBlock();
        if (materialIndex.HasValue)
        {
            renderer.GetPropertyBlock(block, materialIndex.Value);
        }
        else
        {
            renderer.GetPropertyBlock(block);
        }

        return block.isEmpty ? Color.clear : block.GetColor("_BaseColor");
    }

    // Worn before the body has loaded, the pieces show once it attaches; each part hangs from its position's bone in
    // the piece's colour, a Trim part in the player's, and none has a collider (Prototype Content §2).
    [UnityTest]
    public IEnumerator Wear_OnTheAdventurer_HangsEachPositionsStandIn_FromItsBone_InItsColour()
    {
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Adventurer", "character_adventurer", m_catalog, Tint);
        view.Wear(FullLoadout);

        yield return WaitForBody(view);
        yield return null;

        string Bones(EquipmentSlot position)
        {
            return string.Join(", ", view.WornParts(position).Select(part => part.transform.parent.name));
        }

        Assert.That(view.WornParts(EquipmentSlot.Weapon), Is.Empty, "the weapon is held, not worn");
        Assert.That(Bones(EquipmentSlot.Armor), Is.EqualTo("Chest, Chest"));
        Assert.That(Bones(EquipmentSlot.Headgear), Is.EqualTo("Head, Head"));
        Assert.That(Bones(EquipmentSlot.Shield), Is.EqualTo("LeftLowerArm, LeftLowerArm"));
        Assert.That(Bones(EquipmentSlot.Garment), Is.EqualTo("UpperChest, UpperChest"));
        Assert.That(Bones(EquipmentSlot.Footwear), Is.EqualTo("LeftFoot, RightFoot"));
        Assert.That(Bones(EquipmentSlot.Accessory1), Is.EqualTo("Hips"));
        Assert.That(Bones(EquipmentSlot.Accessory2), Is.EqualTo("Hips"));
        foreach (EquipmentSlot position in EquipmentPositions.All.Skip(1))
        {
            Color colour = WornStandIns.ColourOf(FullLoadout[(int)position - 1]);
            foreach (GameObject part in view.WornParts(position))
            {
                Renderer renderer = part.GetComponent<Renderer>();
                bool isTrim = part.name.EndsWith(" Trim");
                Color shown = isTrim ? BlockColour(renderer, 0) : renderer.sharedMaterial.color;
                Assert.That(Difference(shown, isTrim ? Tint : colour), Is.LessThan(ColourTolerance), part.name);
            }
        }

        Vector3 Local(EquipmentSlot position)
        {
            return view.transform.InverseTransformPoint(view.WornParts(position)[0].transform.position);
        }

        Assert.That(Local(EquipmentSlot.Accessory1).x, Is.GreaterThan(0f), "accessory 1 on the left hip");
        Assert.That(Local(EquipmentSlot.Accessory2).x, Is.LessThan(0f), "accessory 2 on the right");
        Assert.That(Local(EquipmentSlot.Headgear).y, Is.GreaterThan(1.5f), "the cap on the head");
        Assert.That(view.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(view.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(12), "the twelve parts");
    }

    // A change of piece redraws its position alone, and taking it off leaves it bare (Prototype Content §2).
    [UnityTest]
    public IEnumerator Wear_AnotherPiece_RedrawsItsPosition_AndNothingTakesItOff()
    {
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Vanguard", "character_vanguard", m_catalog, Tint);
        yield return WaitForBody(view);
        view.Wear(FullLoadout);
        GameObject boot = view.WornParts(EquipmentSlot.Footwear)[0];
        GameObject cap = view.WornParts(EquipmentSlot.Headgear)[0];

        ItemDefinitionId[] swapped = FullLoadout.ToArray();
        swapped[(int)EquipmentSlot.Headgear - 1] = new ItemDefinitionId("item.headgear.grotto_circlet");
        view.Wear(swapped);
        GameObject newCap = view.WornParts(EquipmentSlot.Headgear)[0];
        Color newColour = newCap.GetComponent<Renderer>().sharedMaterial.color;
        bool isBootKept = view.WornParts(EquipmentSlot.Footwear)[0] == boot;
        view.Wear(new ItemDefinitionId[EquipmentPositions.All.Count]);
        yield return null;

        Assert.That(
            Difference(newColour, WornStandIns.ColourOf(new ItemDefinitionId("item.headgear.grotto_circlet"))),
            Is.LessThan(ColourTolerance));
        Assert.That(isBootKept, Is.True, "only the headgear was redrawn");
        Assert.That(EquipmentPositions.All.Select(position => view.WornParts(position).Count), Is.All.EqualTo(0));
        Assert.That(cap == null && boot == null && newCap == null, Is.True, "every part destroyed");
        Assert.That(view.GetComponentsInChildren<MeshRenderer>(), Is.Empty);
    }

    // A frozen body's tint lies over its stand-ins too, its Trim parts keeping the player's colour; when the ailment
    // ends they show their own colours again (Prototype Content §2).
    [UnityTest]
    public IEnumerator Wear_WhileFrozen_TakesTheTint_AndTheTrimKeepsThePlayersColour()
    {
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Arcanist", "character_arcanist", m_catalog, Tint);
        yield return WaitForBody(view);
        view.ShowAilments(EntityStateFlags.Frozen);

        view.Wear(FullLoadout);
        Renderer shell = view.WornParts(EquipmentSlot.Armor)[0].GetComponent<Renderer>();
        Renderer band = view.WornParts(EquipmentSlot.Armor)[1].GetComponent<Renderer>();
        Color frozenShell = BlockColour(shell);
        Color frozenBand = BlockColour(band, 0);
        view.ShowAilments(EntityStateFlags.None);

        Assert.That(
            Difference(frozenShell, EntityView.FrozenTint),
            Is.LessThan(ColourTolerance),
            "frozen with the body");
        Assert.That(Difference(frozenBand, Tint), Is.LessThan(ColourTolerance), "its Trim keeps the player's colour");
        Assert.That(BlockColour(shell), Is.EqualTo(Color.clear), "the ailment's tint gone");
        Assert.That(Difference(BlockColour(band, 0), Tint), Is.LessThan(ColourTolerance));
    }

    [UnityTest]
    public IEnumerator Wear_OnAPlaceholder_DrawsNothing()
    {
        LogAssert.Expect(LogType.Warning, new Regex("EntityViewKeyMissing: Addressables key 'no_such_body'"));
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Unknown", "no_such_body", m_catalog, Tint);
        yield return WaitForBody(view);

        view.Wear(FullLoadout);

        Assert.That(view.IsPlaceholder, Is.True);
        Assert.That(EquipmentPositions.All.Select(position => view.WornParts(position).Count), Is.All.EqualTo(0));
    }
}
}
