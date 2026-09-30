using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     Entity bodies come from Addressables by their content key; in the editor that reads the Asset Database.
/// </summary>
public sealed class EntityViewCatalogTests
{
    // Real time, not frames: in batch mode frames are not throttled, and 300 of them pass before the first load ends.
    private const float TimeoutSeconds = 10f;

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

    [UnityTest]
    public IEnumerator Create_ForAKnownKey_AttachesThePrefabWithoutColliders()
    {
        m_catalog = new EntityViewCatalog();
        m_view = EntityView.Create("Slime", "monster_training_slime", m_catalog, null);

        yield return WaitForBody(m_view);

        Assert.That(m_view.HasBody, Is.True);
        Assert.That(m_view.IsPlaceholder, Is.False);
        Assert.That(m_view.GetComponentsInChildren<Renderer>(), Is.Not.Empty, "a mesh or a skinned mesh");
        Assert.That(m_view.GetComponentsInChildren<Collider>(true), Is.Empty);
    }

    [UnityTest]
    public IEnumerator Create_ForAnUnknownKey_ShowsAPlaceholderAndWarns()
    {
        LogAssert.Expect(LogType.Warning, new Regex("EntityViewKeyMissing: Addressables key 'no_such_view'"));
        m_catalog = new EntityViewCatalog();
        m_view = EntityView.Create("Unknown", "no_such_view", m_catalog, null);

        yield return WaitForBody(m_view);

        Assert.That(m_view.HasBody, Is.True);
        Assert.That(m_view.IsPlaceholder, Is.True);
        Assert.That(m_view.GetComponentsInChildren<Collider>(true), Is.Empty);
    }

    [UnityTest]
    public IEnumerator Create_ForAGrayboxBody_KeepsTodaysHeights_AndPosesAPivotAboveTheBody()
    {
        m_catalog = new EntityViewCatalog();
        m_view = EntityView.Create("Vanguard", "character_vanguard", m_catalog, null);

        yield return WaitForBody(m_view);

        Assert.That(
            (m_view.OverheadHeight, m_view.PickCenterHeight, m_view.PickRadius),
            Is.EqualTo((EntityView.DefaultOverheadHeight, EntityPicker.PickHeight, EntityPicker.PickRadius)));
        Assert.That(m_view.ProjectileOrigin(), Is.EqualTo(new Vector3(0f, EntityView.DefaultProjectileHeight, 0f)));
        Transform body = m_view.transform.Find("Pose/Body");
        Assert.That(body, Is.Not.Null, "the body hangs under the pose pivot");
        Vector3 rest = body.localPosition;
        m_view.SetCombatPose(1f, 0f, false);
        Assert.That(body.localPosition, Is.EqualTo(rest), "the body's own root stays where it was");
        Assert.That(body.parent.localPosition.z, Is.EqualTo(0.35f).Within(0.0001f), "the pivot lunges");
        m_view.SetCombatPose(0f, 0f, true);
        Assert.That(body.parent.localScale.y, Is.EqualTo(0.3f).Within(0.0001f), "the pivot lies flat");
    }

    private static IEnumerator WaitForBody(EntityView view)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (!view.HasBody && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
}
