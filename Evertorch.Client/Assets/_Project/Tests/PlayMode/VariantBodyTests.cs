using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A variant monster's body (Prototype Content §2): drawn at its scale through a node of its own, which neither the
///     combat pose nor its clips write; its anchors, pick, and defaults sized with it; its tint over the whole body;
///     and its target ring and health bar widened with it, the bar to twice its width at most.
/// </summary>
public sealed class VariantBodyTests
{
    // Real time, not frames: in batch mode frames are not throttled.
    private const float TimeoutSeconds = 10f;
    private const float MonarchScale = 2.6f;
    private const float CrawlerScale = 1.2f;

    private static readonly EntityId Local = new(100);
    private static readonly EntityId Monarch = new(300);
    private static readonly EntityId Crawler = new(301);

    private readonly List<Object> m_created = new();
    private EntityViewCatalog? m_catalog;
    private CombatPresenter? m_presenter;

    [TearDown]
    public void TearDown()
    {
        m_presenter?.Dispose();
        m_presenter = null;
        foreach (HealthBar bar in Object.FindObjectsByType<HealthBar>(FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(bar.gameObject);
        }

        for (int index = m_created.Count - 1; index >= 0; index--)
        {
            if (m_created[index] != null)
            {
                Object.DestroyImmediate(m_created[index]);
            }
        }

        m_created.Clear();
        m_catalog?.Dispose();
        m_catalog = null;
    }

    private EntityView Create(string key, float scale = 1f, Color? bodyTint = null)
    {
        m_catalog ??= new EntityViewCatalog();
        var view = EntityView.Create(key, key, m_catalog, null, scale, bodyTint);
        m_created.Add(view.gameObject);
        return view;
    }

    private static IEnumerator WaitForBodies(params EntityView[] views)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (views.Any(view => !view.HasBody) && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static float HeightOf(EntityView view)
    {
        return view.GetComponentsInChildren<Renderer>().Max(renderer => renderer.bounds.size.y);
    }

    // A walk, a swing, a flinch, and a death, the same for both bodies but for the scale.
    private static BodyCue[] Cues(EntityView view, float scale)
    {
        return new[]
        {
            new BodyCue { Speed = 4f, Travelled = 1.3, BodyScale = scale },
            new BodyCue
            {
                HasSwing = true, SwingSince = 0.2, Impact = 0.5, AttackClip = view.AttackClip, BodyScale = scale
            },
            new BodyCue { HasHit = true, HitSince = 0.1, BodyScale = scale },
            new BodyCue { IsDead = true, DeathSince = 0.5, BodyScale = scale }
        };
    }

    private static ClientWorld CreateWorld()
    {
        var cells = new NavigationCell[8 * 8];
        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = NavigationCell.Level(NavigationSurface.Floor, 0f);
        }

        var entered = new WorldEntered(
            new MapDefinitionId("map.umbral_grotto"),
            1,
            Local,
            new JobDefinitionId("job.adventurer"),
            0,
            new WorldPosition(2.5f, 0f, 2.5f),
            new WorldDirection(0f, 1f),
            5f,
            71,
            71,
            1.5f,
            0,
            new CharacterId(1),
            1,
            0,
            30,
            24,
            24);
        var world = new ClientWorld(new NavigationGrid(8, 8, 1f, 0f, 0f, 0.3f, 0.4f, cells), entered, 20);
        foreach ((EntityId entity, string definition) in new[]
                 {
                     (Monarch, "monster.slime_monarch"), (Crawler, "monster.grotto_crawler")
                 })
        {
            world.OnSpawn(
                new EntitySpawn(
                    entity,
                    EntityKind.Monster,
                    definition,
                    new WorldPosition(4.5f, 0f, 2.5f),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.None,
                    1000));
        }

        return world;
    }

    [UnityTest]
    public IEnumerator Create_AtTheMonarchsScale_SizesTheSlime_ItsAnchorsAndItsPick_ThroughItsClips()
    {
        EntityView slime = Create("monster_training_slime");
        EntityView monarch = Create("monster_training_slime", MonarchScale);
        yield return WaitForBodies(slime, monarch);
        monarch.transform.position = new Vector3(10f, 0f, 0f);
        Transform scale = monarch.transform.Find("Pose/Scale");

        Assert.That(monarch.HasClips, Is.True, "the delivered slime plays clips");
        Assert.That(monarch.transform.Find("Pose/Scale/Body"), Is.Not.Null, "the body hangs under its scale");
        Assert.That(monarch.Scale, Is.EqualTo(MonarchScale));
        Assert.That(monarch.OverheadHeight, Is.EqualTo(slime.OverheadHeight * MonarchScale).Within(0.001f));
        Assert.That(monarch.PickCenterHeight, Is.EqualTo(slime.PickCenterHeight * MonarchScale).Within(0.001f));
        Assert.That(monarch.PickRadius, Is.EqualTo(slime.PickRadius * MonarchScale).Within(0.001f));
        Assert.That(
            monarch.ProjectileArrival().y,
            Is.EqualTo(slime.ProjectileArrival().y * MonarchScale).Within(0.001f));
        BodyCue[] large = Cues(monarch, MonarchScale);
        BodyCue[] small = Cues(slime, 1f);
        for (int index = 0; index < large.Length; index++)
        {
            monarch.Animate(large[index], index, 0.02f);
            slime.Animate(small[index], index, 0.02f);
            yield return null;

            Assert.That(scale.localScale, Is.EqualTo(Vector3.one * MonarchScale), $"cue {index}: the scale stays");
            Assert.That(HeightOf(monarch) / HeightOf(slime), Is.EqualTo(MonarchScale).Within(0.05f), $"cue {index}");
        }
    }

    [UnityTest]
    public IEnumerator Create_AtAScale_WithNoBody_SizesTheDefaults_AndStandsThePlaceholderOnTheFloor()
    {
        LogAssert.Expect(LogType.Warning, new Regex("EntityViewKeyMissing: Addressables key 'no_such_view'"));
        EntityView view = Create("no_such_view", 2f);

        Assert.That(
            (view.OverheadHeight, view.PickCenterHeight, view.PickRadius),
            Is.EqualTo(
                (EntityView.DefaultOverheadHeight * 2f, EntityPicker.PickHeight * 2f, EntityPicker.PickRadius * 2f)));
        yield return WaitForBodies(view);
        Renderer placeholder = view.GetComponentsInChildren<Renderer>().Single();
        Assert.That(view.IsPlaceholder, Is.True);
        Assert.That(placeholder.bounds.min.y, Is.EqualTo(0f).Within(0.001f), "on the floor");
        Assert.That(placeholder.bounds.size.y, Is.EqualTo(3.2f).Within(0.001f), "twice its 1.6 m");
        Assert.That(
            view.ProjectileArrival().y,
            Is.EqualTo(EntityView.DefaultProjectileHeight * 2f).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator Create_WithABodyTint_ColoursEveryRenderer_EvenABodyWithTrimSlots()
    {
        var violet = new Color(0.36f, 0.23f, 0.55f);
        EntityView view = Create("character_adventurer", 1f, violet);
        yield return WaitForBodies(view);

        Renderer[] renderers = view.GetComponentsInChildren<Renderer>();
        var block = new MaterialPropertyBlock();
        Assert.That(view.GetComponentInChildren<EntityBody>().TrimSlots, Is.Not.Empty, "a body with Trim slots");
        Assert.That(renderers, Is.Not.Empty);
        foreach (Renderer renderer in renderers)
        {
            renderer.GetPropertyBlock(block);
            Assert.That(
                block.GetColor("_BaseColor"),
                Is.EqualTo(violet).Using(new ColorEqualityComparer(1e-4f)),
                renderer.name);
        }
    }

    [UnityTest]
    public IEnumerator Present_UnderAndOverALargerBody_WidensTheRing_AndTheBarToTwiceAtMost()
    {
        EntityView monarch = Create("monster_training_slime", MonarchScale);
        EntityView crawler = Create("monster_forest_crawler", CrawlerScale);
        yield return WaitForBodies(monarch, crawler);
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m_created.Add(material);
        EntityView local = new GameObject("Local").AddComponent<EntityView>();
        m_created.Add(local.gameObject);
        var ring = TargetMarker.Create(material);
        m_created.Add(ring.gameObject);
        m_presenter = new CombatPresenter(CreateWorld(), 0.05, material);

        var remotes = new Dictionary<EntityId, EntityView> { [Monarch] = monarch, [Crawler] = crawler };

        m_presenter.Present(local, remotes, null);
        ring.Show(new WorldPosition(0f, 0f, 0f), monarch.Scale);

        Assert.That(m_presenter.TryGetHealthBar(Monarch, out HealthBar? large), Is.True);
        Assert.That(m_presenter.TryGetHealthBar(Crawler, out HealthBar? small), Is.True);
        Assert.That(large!.transform.localScale.x, Is.EqualTo(2f), "twice the width at most");
        Assert.That(small!.transform.localScale.x, Is.EqualTo(CrawlerScale), "as wide as the body is large");
        Assert.That(ring.transform.localScale.x, Is.EqualTo(1.4f * MonarchScale).Within(0.0001f));
    }
}
}
