using System;
using System.Collections;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The delivered slime's attack drawn through the real combat presenter (Gameplay Systems §8): its impact frame
///     lands with the shown damage, and its pose depends on timeline time alone, at any frame rate.
/// </summary>
public sealed class BodyAnimationTimingTests
{
    private const double TickSeconds = 0.05;
    private const float TimeoutSeconds = 10f;

    // The training slime's motion: half its 1,200 ms interval (Gameplay Systems §7).
    private static readonly AttackTiming SlimeSwing = new(
        TimeSpan.FromMilliseconds(1200),
        TimeSpan.FromMilliseconds(600),
        TimeSpan.FromMilliseconds(600),
        TimeSpan.FromMilliseconds(300));

    private static readonly EntityId Local = new(100);
    private static readonly EntityId Slime = new(300);
    private static readonly EntityId Other = new(400);

    private readonly List<Object> m_created = new();
    private CombatPresenter? m_presenter;
    private EntityViewCatalog? m_catalog;

    [TearDown]
    public void TearDown()
    {
        m_presenter?.Dispose();
        m_presenter = null;
        foreach (FloatingNumber number in Object.FindObjectsByType<FloatingNumber>(FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(number.gameObject);
        }

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

    private static ClientWorld CreateWorld()
    {
        var cells = new NavigationCell[8 * 8];
        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = NavigationCell.Level(NavigationSurface.Floor, 0f);
        }

        var grid = new NavigationGrid(8, 8, 1f, 0f, 0f, 0.3f, 0.4f, cells);
        var entered = new WorldEntered(
            new MapDefinitionId("map.training_ground"),
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
        var world = new ClientWorld(grid, entered, 20);
        world.OnSpawn(
            new EntitySpawn(
                Slime,
                EntityKind.Monster,
                "monster.training_slime",
                new WorldPosition(4.5f, 0f, 2.5f),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000));
        world.OnSpawn(
            new EntitySpawn(
                Other,
                EntityKind.Player,
                "job.adventurer",
                new WorldPosition(5.5f, 0f, 2.5f),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                0));
        return world;
    }

    private IEnumerator CreateSlimeView(Action<EntityView> ready)
    {
        m_catalog = new EntityViewCatalog();
        var view = EntityView.Create("Slime", "monster_training_slime", m_catalog, null);
        m_created.Add(view.gameObject);
        view.transform.position = new Vector3(4.5f, 0f, 2.5f);
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (!view.HasBody && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.That(view.HasClips, Is.True, "the delivered slime");
        ready(view);
    }

    private CombatPresenter CreatePresenter(ClientWorld world)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m_created.Add(material);
        m_presenter = new CombatPresenter(world, TickSeconds, material);
        return m_presenter;
    }

    // The slime swings at another player from tick 40, its impact due 0.6 s later: the server resolves it on the first
    // tick at or after that, tick 52, and sends the damage with that tick, which the presenter shows on the same
    // interpolated timeline. When the number shows, the clip stands within one tick of its impact frame.
    [UnityTest]
    public IEnumerator Attack_WhenItsDamageIsShown_StandsAtItsImpactFrame_WithinOneTick()
    {
        EntityView slime = null!;
        yield return CreateSlimeView(view => slime = view);
        ClientWorld world = CreateWorld();
        CombatPresenter presenter = CreatePresenter(world);
        EntityView local = new GameObject("Local").AddComponent<EntityView>();
        m_created.Add(local.gameObject);
        EntityView target = new GameObject("Other").AddComponent<EntityView>();
        m_created.Add(target.gameObject);
        var remotes = new Dictionary<EntityId, EntityView> { { Slime, slime }, { Other, target } };
        world.OnAttackStarted(new AttackStarted(Slime, Other, 40, SlimeSwing));
        world.OnDamage(new Damage(Slime, Other, CombatResult.Hit, 3, 52, 0));

        const float step = 1f / 144f;
        for (int frame = 0; frame < 144 * 10 && presenter.NumbersShown == 0; frame++)
        {
            world.Advance(step);
            presenter.Present(local, remotes, null);
        }

        BodyAnimator animator = slime.BodyAnimator!;
        double impactFrame = animator.CurrentTime * BodyClips.FramesPerSecond;
        double framesPerSecond = 24.0 / SlimeSwing.Impact.TotalSeconds;
        Assert.That(presenter.NumbersShown, Is.EqualTo(1), "the damage was shown");
        Assert.That(animator.CurrentClip, Is.EqualTo("attack"));
        Assert.That(
            impactFrame,
            Is.InRange(24.0, 24.0 + (TickSeconds + step) * framesPerSecond),
            "within one tick, and one drawn frame, of frame 24");
    }

    // The same swing drawn at 30 and at 144 frames a second stands in the same pose at the same timeline time.
    [UnityTest]
    public IEnumerator Attack_DrawnAtThirtyOrAHundredFortyFourFrames_IsTheSamePoseAtTheSameTime()
    {
        EntityView slime = null!;
        yield return CreateSlimeView(view => slime = view);
        var poses = new List<(string Clip, double Time)>();
        foreach (int framesPerSecond in new[] { 30, 144 })
        {
            ClientWorld world = CreateWorld();
            CombatPresenter presenter = CreatePresenter(world);
            EntityView local = new GameObject("Local").AddComponent<EntityView>();
            m_created.Add(local.gameObject);
            var remotes = new Dictionary<EntityId, EntityView> { { Slime, slime } };
            world.OnAttackStarted(new AttackStarted(Slime, Other, 40, SlimeSwing));
            presenter.Present(local, remotes, null);

            // Two seconds and a sixth: a whole number of frames at both rates, part way through the swing.
            int frames = framesPerSecond * 13 / 6;
            for (int frame = 0; frame < frames; frame++)
            {
                world.Advance(1f / framesPerSecond);
                presenter.Present(local, remotes, null);
            }

            poses.Add((slime.BodyAnimator!.CurrentClip, slime.BodyAnimator.CurrentTime));
            presenter.Dispose();
            m_presenter = null;
        }

        Assert.That(poses[0].Clip, Is.EqualTo("attack"), "part way through the swing");
        Assert.That(poses[1].Clip, Is.EqualTo(poses[0].Clip));
        Assert.That(poses[1].Time, Is.EqualTo(poses[0].Time).Within(0.001), "the same time in the clip");
    }
}
}
