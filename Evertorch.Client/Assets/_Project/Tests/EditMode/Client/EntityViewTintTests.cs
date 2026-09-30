using System.IO;
using System.Linq;
using Evertorch.Client.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The player tint (Prototype Content §2): an art outfit's Trim slot alone, a graybox body whole.
/// </summary>
[TestFixture]
public sealed class EntityViewTintTests
{
    [TearDown]
    public void DestroyBody()
    {
        if (m_body != null)
        {
            Object.DestroyImmediate(m_body);
        }
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color Tint = new(0.9f, 0.1f, 0.2f);

    private GameObject? m_body;

    // A property block keeps a colour to float precision after its colour-space round trip.
    private static bool IsTint(Color color)
    {
        return Vector4.Distance(color, Tint) < 0.001f;
    }

    private static string BodyPath(string key)
    {
        string staged = ArtPaths.StagedPrefabPath(key);
        return File.Exists(staged) ? staged : $"Assets/_Project/Prefabs/{key}.prefab";
    }

    [Test]
    public void ApplyTint_ForABodyWithoutTrimSlots_ColoursEveryRenderer()
    {
        m_body = new GameObject("Graybox");
        GameObject.CreatePrimitive(PrimitiveType.Capsule).transform.SetParent(m_body.transform, false);
        GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(m_body.transform, false);

        EntityView.ApplyTint(m_body, null, Tint);

        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in m_body.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(block);
            Assert.That(IsTint(block.GetColor(BaseColorId)), Is.True, renderer.name);
        }
    }

    [Test]
    public void ApplyTint_ForAnArtOutfit_ColoursItsTrimSlotAlone()
    {
        m_body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath("character_adventurer")));
        Assert.That(m_body.TryGetComponent(out EntityBody anchors), Is.True);

        EntityView.ApplyTint(m_body, anchors, Tint);

        EntityBody.TrimSlot trim = anchors.TrimSlots.Single();
        Renderer renderer = trim.Renderer!;
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block, trim.MaterialIndex);
        Assert.That(IsTint(block.GetColor(BaseColorId)), Is.True, "the Trim slot takes the tint");
        foreach (int slot in Enumerable.Range(0, renderer.sharedMaterials.Length)
                     .Where(slot => slot != trim.MaterialIndex))
        {
            renderer.GetPropertyBlock(block, slot);
            Assert.That(block.isEmpty, Is.True, $"slot {slot} keeps its own colour");
        }

        renderer.GetPropertyBlock(block);
        Assert.That(block.isEmpty, Is.True, "nothing tints the whole renderer");
    }
}
}
