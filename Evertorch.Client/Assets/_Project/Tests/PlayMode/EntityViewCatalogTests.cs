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
        Assert.That(m_view.GetComponentsInChildren<SkinnedMeshRenderer>(), Is.Not.Empty, "the delivered slime");
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

    [UnityTest]
    public IEnumerator Create_ForTheAdventurer_ReadsItsAnchors_AndPlaysItsClips()
    {
        m_catalog = new EntityViewCatalog();
        m_view = EntityView.Create("Adventurer", "character_adventurer", m_catalog, null);

        yield return WaitForBody(m_view);

        Assert.That(m_view.HasClips, Is.True, "the delivered body plays clips");
        Assert.That(m_view.OverheadHeight, Is.EqualTo(1.97f).Within(0.001f));
        Assert.That(m_view.PickCenterHeight, Is.EqualTo(0.85f).Within(0.001f));
        Assert.That(m_view.PickRadius, Is.EqualTo(EntityBody.MaxPickRadius), "1.1 m delivered, capped");
        yield return null;
        var dead = new BodyCue { IsDead = true, DeathSince = double.PositiveInfinity };
        m_view.Animate(dead, 1.0, 0.02f);
        Assert.That(
            (m_view.BodyAnimator!.CurrentClip, m_view.BodyAnimator.CurrentWeight),
            Is.EqualTo(("death", 1f)),
            "a body first seen dead lies there at once, with no fade from its first idle");
        var cue = new BodyCue { HasSwing = true, SwingSince = 0.47, Impact = 0.47, AttackClip = m_view.AttackClip };
        m_view.Animate(cue, 0.0, 0.02f);
        BodyAnimator animator = m_view.BodyAnimator!;
        Assert.That(animator.CurrentClip, Is.EqualTo("attack_unarmed"));
        Assert.That(animator.CurrentTime * BodyClips.FramesPerSecond, Is.EqualTo(24.0).Within(0.0001));
        Transform pose = m_view.transform.Find("Pose");
        m_view.SetCombatPose(1f, 1f, true);
        Assert.That(pose.localScale, Is.EqualTo(Vector3.one), "a body with clips is never flattened");
        Assert.That(pose.localPosition, Is.EqualTo(Vector3.zero), "nor lunged");
    }

    // The sword on the ground is picked by the sphere every drop has, so a click on a monster standing over it still
    // takes the monster (Prototype Content §4).
    [UnityTest]
    public IEnumerator Create_ForTheSwordOnTheGround_PicksLikeAnyDrop()
    {
        m_catalog = new EntityViewCatalog();
        m_view = EntityView.Create("Sword", "pickup_training_sword", m_catalog, null);

        yield return WaitForBody(m_view);

        Assert.That(m_view.IsPlaceholder, Is.False, "the delivered sword");
        Assert.That(
            (m_view.PickCenterHeight, m_view.PickRadius),
            Is.EqualTo((EntityPicker.PickHeight, EntityPicker.PickRadius)));
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
