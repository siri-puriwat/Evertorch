using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A delivered body wearing ArtDelivery-03's pieces (Prototype Content §2.1): each piece's parts hang on the body's
///     sockets at no offset, the second accessory's on the right hip, the Trim parts in the player's colour, without a
///     collider; a change redraws its position alone, a late load is dropped, an ailment's tint lies over them as over
///     the body, and a missing model or socket leaves its position empty with one warning.
/// </summary>
public sealed class WornPieceTests
{
    // Real time, not frames: in batch mode frames are not throttled.
    private const float TimeoutSeconds = 10f;

    private const float ColourTolerance = 0.002f;

    private static readonly Color Tint = new(0.2f, 0.45f, 1f);

    private static readonly string[] FullLoadout =
    {
        string.Empty, "worn_leather_armor", "worn_trail_cap", "worn_wooden_buckler", "worn_travel_cape",
        "worn_trail_boots", "worn_traveler_charm", "worn_grotto_charm"
    };

    private EntityViewCatalog? m_catalog;
    private EntityView? m_view;
    private EntityView? m_second;

    [TearDown]
    public void TearDown()
    {
        foreach (EntityView? view in new[] { m_view, m_second })
        {
            if (view != null)
            {
                Object.DestroyImmediate(view.gameObject);
            }
        }

        m_catalog?.Dispose();
    }

    private static IEnumerator WaitUntil(Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static bool IsDressed(EntityView view, string[] keys)
    {
        return EquipmentPositions.All.Skip(1)
            .All(position => keys[(int)position - 1].Length == 0 || view.WornParts(position).Count > 0);
    }

    // The worn model a part came from, by its material.
    private static string PieceOf(GameObject part)
    {
        string material = part.GetComponent<Renderer>().sharedMaterials
            .Select(shared => shared.name)
            .First(name => name.StartsWith("Art_worn_"));
        return material.Substring(4, material.LastIndexOf('_') - 4);
    }

    private static string Sockets(EntityView view, EquipmentSlot position)
    {
        return string.Join(", ", view.WornParts(position).Select(part => part.transform.parent.name));
    }

    // A colour read back from a block may differ in its last bits from the one written.
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

    private static int TrimSlotOf(GameObject part)
    {
        return Array.FindIndex(
            part.GetComponent<Renderer>().sharedMaterials,
            material => material.name.EndsWith("_Trim"));
    }

    // Worn before the body has loaded, the pieces show once it attaches, each part on its socket at no offset, a Trim
    // part's slot in the player's colour, and none with a collider.
    [UnityTest]
    public IEnumerator Wear_OnTheAdventurer_HangsEachPiecesParts_OnTheirSockets_AtNoOffset()
    {
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Adventurer", "character_adventurer", m_catalog, Tint);
        view.Wear(FullLoadout);

        yield return WaitUntil(() => view.HasBody && IsDressed(view, FullLoadout));

        Assert.That(view.WornParts(EquipmentSlot.Weapon), Is.Empty, "the weapon is held, not worn");
        Assert.That(Sockets(view, EquipmentSlot.Armor), Is.EqualTo("Socket_Chest"));
        Assert.That(Sockets(view, EquipmentSlot.Headgear), Is.EqualTo("Socket_Head"));
        Assert.That(Sockets(view, EquipmentSlot.Shield), Is.EqualTo("Socket_Shield"));
        Assert.That(Sockets(view, EquipmentSlot.Garment), Is.EqualTo("Socket_Back"));
        Assert.That(Sockets(view, EquipmentSlot.Footwear), Is.EqualTo("Socket_Foot_L, Socket_Foot_R"));
        Assert.That(Sockets(view, EquipmentSlot.Accessory1), Is.EqualTo("Socket_Hip_L"));
        Assert.That(Sockets(view, EquipmentSlot.Accessory2), Is.EqualTo("Socket_Hip_R"), "the same charm's right hip");
        foreach (EquipmentSlot position in EquipmentPositions.All.Skip(1))
        {
            foreach (GameObject part in view.WornParts(position))
            {
                string what = $"{position} {part.name}";
                Assert.That(PieceOf(part), Is.EqualTo(FullLoadout[(int)position - 1]), what);
                Assert.That(
                    (part.transform.localPosition.magnitude < 0.0001f,
                        Quaternion.Angle(part.transform.localRotation, Quaternion.identity) < 0.01f,
                        Vector3.Distance(part.transform.localScale, Vector3.one) < 0.0001f),
                    Is.EqualTo((true, true, true)),
                    $"{what}: at no offset");
                int trim = TrimSlotOf(part);
                if (trim >= 0)
                {
                    Assert.That(
                        Difference(BlockColour(part.GetComponent<Renderer>(), trim), Tint),
                        Is.LessThan(ColourTolerance),
                        $"{what}: its Trim in the player's colour");
                }
            }
        }

        Vector3 Local(EquipmentSlot position)
        {
            return view.transform.InverseTransformPoint(view.WornParts(position)[0].transform.position);
        }

        Assert.That(Local(EquipmentSlot.Accessory1).x, Is.GreaterThan(0f), "accessory 1 on the left hip");
        Assert.That(Local(EquipmentSlot.Accessory2).x, Is.LessThan(0f), "accessory 2 on the right");
        Assert.That(Local(EquipmentSlot.Headgear).y, Is.GreaterThan(1.5f), "the cap on the head");
        Assert.That(TrimSlotOf(view.WornParts(EquipmentSlot.Armor)[0]), Is.GreaterThanOrEqualTo(0), "armor's band");
        Assert.That(view.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(view.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(8), "the eight parts");
    }

    // A change of piece redraws its position alone, and taking everything off leaves the body bare.
    [UnityTest]
    public IEnumerator Wear_AnotherPiece_RedrawsItsPositionAlone_AndNothingTakesItOff()
    {
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Vanguard", "character_vanguard", m_catalog, Tint);
        view.Wear(FullLoadout);
        yield return WaitUntil(() => view.HasBody && IsDressed(view, FullLoadout));
        GameObject boot = view.WornParts(EquipmentSlot.Footwear)[0];
        GameObject cap = view.WornParts(EquipmentSlot.Headgear)[0];

        string[] swapped = FullLoadout.ToArray();
        swapped[(int)EquipmentSlot.Headgear - 1] = "worn_grotto_circlet";
        view.Wear(swapped);
        yield return WaitUntil(() =>
            view.WornParts(EquipmentSlot.Headgear).Count > 0
            && PieceOf(view.WornParts(EquipmentSlot.Headgear)[0]) == "worn_grotto_circlet");
        GameObject circlet = view.WornParts(EquipmentSlot.Headgear)[0];
        string shown = PieceOf(circlet);
        bool isBootKept = view.WornParts(EquipmentSlot.Footwear)[0] == boot;
        view.Wear(new string[EquipmentPositions.All.Count]);
        yield return null;

        Assert.That(shown, Is.EqualTo("worn_grotto_circlet"));
        Assert.That(cap == null, Is.True, "the cap destroyed");
        Assert.That(isBootKept, Is.True, "only the headgear was redrawn");
        Assert.That(EquipmentPositions.All.Select(position => view.WornParts(position).Count), Is.All.EqualTo(0));
        Assert.That(boot == null && circlet == null, Is.True, "every part destroyed");
        Assert.That(view.GetComponentsInChildren<MeshRenderer>(), Is.Empty);
    }

    // A piece changed again before its model loads shows only the last; the first load, finishing later, is dropped.
    [UnityTest]
    public IEnumerator Wear_ChangedBeforeItsModelLoads_ShowsOnlyTheLastPiece()
    {
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Arcanist", "character_arcanist", m_catalog, Tint);
        yield return WaitUntil(() => view.HasBody);
        string[] cap = new string[EquipmentPositions.All.Count];
        cap[(int)EquipmentSlot.Headgear - 1] = "worn_trail_cap";
        string[] circlet = new string[EquipmentPositions.All.Count];
        circlet[(int)EquipmentSlot.Headgear - 1] = "worn_grotto_circlet";

        view.Wear(cap);
        view.Wear(circlet);
        // The catalog answers its callers in order, so once this answer comes the view's own, older one has come too.
        bool isCapLoaded = false;
        m_catalog.Request("worn_trail_cap", _ => isCapLoaded = true);
        yield return WaitUntil(() => isCapLoaded && view.WornParts(EquipmentSlot.Headgear).Count > 0);
        yield return null;

        Assert.That(
            view.WornParts(EquipmentSlot.Headgear).Select(PieceOf),
            Is.EqualTo(new[] { "worn_grotto_circlet" }));
        Assert.That(view.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(1), "no stray cap");
    }

    // A frozen body's tint lies over its pieces too, their Trim slots keeping the player's colour; when the ailment
    // ends they show their own colours again.
    [UnityTest]
    public IEnumerator Wear_WhileFrozen_TakesTheTint_AndTheTrimKeepsThePlayersColour()
    {
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Arcanist", "character_arcanist", m_catalog, Tint);
        yield return WaitUntil(() => view.HasBody);
        view.ShowAilments(EntityStateFlags.Frozen);

        view.Wear(FullLoadout);
        yield return WaitUntil(() => IsDressed(view, FullLoadout));
        GameObject shell = view.WornParts(EquipmentSlot.Armor)[0];
        Renderer renderer = shell.GetComponent<Renderer>();
        int trim = TrimSlotOf(shell);
        Color frozen = BlockColour(renderer);
        Color frozenTrim = BlockColour(renderer, trim);
        view.ShowAilments(EntityStateFlags.None);

        Assert.That(Difference(frozen, EntityView.FrozenTint), Is.LessThan(ColourTolerance), "frozen with the body");
        Assert.That(Difference(frozenTrim, Tint), Is.LessThan(ColourTolerance), "its Trim keeps the player's colour");
        Assert.That(BlockColour(renderer), Is.EqualTo(Color.clear), "the ailment's tint gone");
        Assert.That(Difference(BlockColour(renderer, trim), Tint), Is.LessThan(ColourTolerance));
    }

    // One body's change never releases a model another still wears: the catalog keeps every model it loaded.
    [UnityTest]
    public IEnumerator Wear_TheSamePiece_OnTwoBodies_SurvivesEitherGoing()
    {
        m_catalog = new EntityViewCatalog();
        EntityView first = m_view = EntityView.Create("First", "character_adventurer", m_catalog, Tint);
        EntityView second = m_second = EntityView.Create("Second", "character_vanguard", m_catalog, Tint);
        first.Wear(FullLoadout);
        second.Wear(FullLoadout);
        yield return WaitUntil(() =>
            first.HasBody && second.HasBody && IsDressed(first, FullLoadout) && IsDressed(second, FullLoadout));

        Object.Destroy(first.gameObject);
        m_view = null;
        yield return null;
        first = m_view = EntityView.Create("Third", "character_arcanist", m_catalog, Tint);
        first.Wear(FullLoadout);
        yield return WaitUntil(() => first.HasBody && IsDressed(first, FullLoadout));

        // Unity's own comparison: a destroyed mesh is still a reference, so NUnit's null check would pass it.
        Mesh mesh = second.WornParts(EquipmentSlot.Headgear)[0].GetComponent<MeshFilter>().sharedMesh;
        Assert.That(mesh != null && mesh.vertexCount > 0, Is.True, "the second body's cap keeps its mesh");
        Assert.That(IsDressed(first, FullLoadout), Is.True, "a new body wears the same models");
    }

    [UnityTest]
    public IEnumerator Wear_OnAPlaceholder_DrawsNothing()
    {
        LogAssert.Expect(LogType.Warning, new Regex("EntityViewKeyMissing: Addressables key 'no_such_body'"));
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Unknown", "no_such_body", m_catalog, Tint);
        yield return WaitUntil(() => view.HasBody);

        view.Wear(FullLoadout);
        float settle = Time.realtimeSinceStartup + 0.5f;
        while (Time.realtimeSinceStartup < settle)
        {
            yield return null;
        }

        Assert.That(view.IsPlaceholder, Is.True);
        Assert.That(EquipmentPositions.All.Select(position => view.WornParts(position).Count), Is.All.EqualTo(0));
    }

    // A model that does not resolve leaves its position empty over the intact body, with the catalog's one warning.
    [UnityTest]
    public IEnumerator Wear_AMissingModel_LeavesItsPositionEmpty_WithOneWarning()
    {
        LogAssert.Expect(LogType.Warning, new Regex("EntityViewKeyMissing: Addressables key 'worn_no_such_piece'"));
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Adventurer", "character_adventurer", m_catalog, Tint);
        EntityView second = m_second = EntityView.Create("Vanguard", "character_vanguard", m_catalog, Tint);
        string[] keys = FullLoadout.ToArray();
        keys[(int)EquipmentSlot.Headgear - 1] = "worn_no_such_piece";

        view.Wear(keys);
        second.Wear(keys);
        yield return WaitUntil(() => view.HasBody && second.HasBody && view.WornParts(EquipmentSlot.Armor).Count > 0
            && second.WornParts(EquipmentSlot.Armor).Count > 0);
        float settle = Time.realtimeSinceStartup + 0.5f;
        while (Time.realtimeSinceStartup < settle)
        {
            yield return null;
        }

        Assert.That(view.WornParts(EquipmentSlot.Headgear), Is.Empty);
        Assert.That(second.WornParts(EquipmentSlot.Headgear), Is.Empty);
        Assert.That(view.IsPlaceholder, Is.False, "the body stays");
        Assert.That(view.WornParts(EquipmentSlot.Footwear).Count, Is.EqualTo(2), "the rest still worn");
        LogAssert.NoUnexpectedReceived();
    }

    // A body without the sockets, such as a monster's, wears nothing there, with one warning for every body alike.
    [UnityTest]
    public IEnumerator Wear_OnABodyWithoutTheSocket_LeavesItsPositionEmpty_WithOneWarning()
    {
        LogAssert.Expect(
            LogType.Warning,
            new Regex("WornSocketMissing: 'monster_training_slime' has no Socket_Head for 'worn_trail_cap'"));
        m_catalog = new EntityViewCatalog();
        EntityView view = m_view = EntityView.Create("Slime", "monster_training_slime", m_catalog, null);
        EntityView second = m_second = EntityView.Create("Slime 2", "monster_training_slime", m_catalog, null);
        string[] cap = new string[EquipmentPositions.All.Count];
        cap[(int)EquipmentSlot.Headgear - 1] = "worn_trail_cap";

        view.Wear(cap);
        second.Wear(cap);
        yield return WaitUntil(() => view.HasBody && second.HasBody);
        float settle = Time.realtimeSinceStartup + 0.5f;
        while (Time.realtimeSinceStartup < settle)
        {
            yield return null;
        }

        Assert.That(view.WornParts(EquipmentSlot.Headgear), Is.Empty);
        Assert.That(second.WornParts(EquipmentSlot.Headgear), Is.Empty);
        LogAssert.NoUnexpectedReceived();
    }
}
}
