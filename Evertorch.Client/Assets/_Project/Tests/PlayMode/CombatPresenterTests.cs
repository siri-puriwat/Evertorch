using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The presenter turns the world's combat events into views, on the monster's interpolated timeline.
/// </summary>
public sealed class CombatPresenterTests
{
    private static readonly EntityId Local = new(100);
    private static readonly EntityId Slime = new(300);
    private static readonly SkillDefinitionId FirstAid = new("skill.first_aid");
    private static readonly SkillDefinitionId Strike = new("skill.strike");

    private readonly List<Object> m_created = new();
    private CombatPresenter? m_presenter;

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

        foreach (CastBar bar in
                 Object.FindObjectsByType<CastBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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
    }

    private ClientWorld CreateWorld()
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
        return world;
    }

    private Dictionary<EntityId, EntityView> CreateViews(out EntityView local)
    {
        var localObject = new GameObject("Local");
        var slimeObject = new GameObject("Slime");
        slimeObject.transform.position = new Vector3(4.5f, 0f, 2.5f);
        m_created.Add(localObject);
        m_created.Add(slimeObject);
        local = localObject.AddComponent<EntityView>();
        return new Dictionary<EntityId, EntityView> { { Slime, slimeObject.AddComponent<EntityView>() } };
    }

    private CombatPresenter CreatePresenter(ClientWorld world)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m_created.Add(material);
        m_presenter = new CombatPresenter(world, 0.05, material);
        return m_presenter;
    }

    [Test]
    public void Damage_ToAMonster_ShowsItsNumberAndMovesItsBarWhenItsMomentIsDrawn()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);

        presenter.Present(local, remotes, null);
        Assert.That(presenter.TryGetHealthBar(Slime, out HealthBar? bar), Is.True);
        Assert.That(bar!.gameObject.activeSelf, Is.True);
        Assert.That(bar.ShownRatio, Is.EqualTo(1f));

        world.OnDamage(new Damage(Local, Slime, CombatResult.Hit, 12, 2, 760));
        presenter.Present(local, remotes, null);
        int shownEarly = presenter.NumbersShown;
        float ratioEarly = bar.ShownRatio;
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);

        Assert.That(shownEarly, Is.Zero, "tick 2 is not drawn yet");
        Assert.That(ratioEarly, Is.EqualTo(1f), "the bar waits for the number");
        Assert.That(presenter.NumbersShown, Is.EqualTo(1));
        Assert.That(bar.ShownRatio, Is.EqualTo(0.76f).Within(1e-4f));
        FloatingNumber number = Object.FindAnyObjectByType<FloatingNumber>();
        Assert.That(number, Is.Not.Null);
        Assert.That(number.Text, Is.EqualTo("12"));
    }

    [Test]
    public void OwnCast_DrawsABarOverThePlayerForItsTime_AndItsHealIsAGreenPlusNumber()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);

        world.OnSkillCastStarted(new SkillCastStarted(Local, FirstAid, default, 0, 1000));
        presenter.Present(local, remotes, null);
        presenter.TryGetCastBar(Local, out CastBar? bar);
        bool isShownAtTheStart = bar != null && bar.IsShown;
        float height = bar != null ? bar.transform.position.y : 0f;
        world.Advance(0.5f);
        presenter.Present(local, remotes, null);
        float halfway = bar!.ShownProgress;
        world.Advance(0.55f);
        world.OnSkillResolved(new SkillResolved(Local, Local, FirstAid, SkillOutcome.Healed, 15, 22, 0));
        presenter.Present(local, remotes, null);

        Assert.That(isShownAtTheStart, Is.True);
        Assert.That(height, Is.EqualTo(1.45f).Within(1e-4f), "1.45 m over the caster");
        Assert.That(halfway, Is.EqualTo(0.5f).Within(1e-3f));
        Assert.That(bar.IsShown, Is.False, "the cast time is over");
        FloatingNumber number = Object.FindAnyObjectByType<FloatingNumber>();
        Assert.That(number, Is.Not.Null);
        Assert.That(number.Text, Is.EqualTo("+15"));
        Color color = number.GetComponent<TextMeshPro>().color;
        Assert.That(color.g, Is.GreaterThan(color.r + 0.3f), "green");
    }

    [Test]
    public void OwnCast_Cancelled_HidesItsBar_AndARemoteCastIsDrawnOnItsTimeline()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);

        world.OnSkillCastStarted(new SkillCastStarted(Local, FirstAid, default, 0, 1500));
        world.OnSkillCastStarted(new SkillCastStarted(Slime, Strike, Local, 2, 1000));
        presenter.Present(local, remotes, null);
        presenter.TryGetCastBar(Local, out CastBar? own);
        bool isRemoteBarShownEarly = presenter.TryGetCastBar(Slime, out CastBar? _);
        world.OnLocalCancel();
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);
        presenter.TryGetCastBar(Slime, out CastBar? remote);

        Assert.That(isRemoteBarShownEarly, Is.False, "tick 2 is not drawn yet");
        Assert.That(own!.IsShown, Is.False, "cancelled");
        Assert.That(remote, Is.Not.Null);
        Assert.That(remote!.IsShown, Is.True);
        Assert.That(remote.ShownProgress, Is.EqualTo(0.1f).Within(1e-3f), "0.1 s into the slime's cast");
    }

    [Test]
    public void SkillHit_OnAMonster_ShowsItsNumberAndMovesItsBarWhenItsMomentIsDrawn()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);
        presenter.Present(local, remotes, null);
        presenter.TryGetHealthBar(Slime, out HealthBar? bar);

        world.OnSkillResolved(new SkillResolved(Local, Slime, Strike, SkillOutcome.Hit, 17, 2, 660));
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);

        Assert.That(presenter.NumbersShown, Is.EqualTo(1));
        Assert.That(bar!.ShownRatio, Is.EqualTo(0.66f).Within(1e-4f));
        Assert.That(Object.FindAnyObjectByType<FloatingNumber>().Text, Is.EqualTo("17"));
    }

    [Test]
    public void Death_OfAMonster_HidesItsBarOnceTheDeathIsDrawn()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);
        presenter.Present(local, remotes, null);
        presenter.TryGetHealthBar(Slime, out HealthBar? bar);

        world.OnEntityDied(new EntityDied(Slime, Local, 2));
        presenter.Present(local, remotes, null);
        bool isShownBeforeTheDeathIsDrawn = bar!.gameObject.activeSelf;
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);

        Assert.That(isShownBeforeTheDeathIsDrawn, Is.True);
        Assert.That(bar.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Presentation_ChangesNoCommandIntentOrLock_WhateverTheFrameRate()
    {
        List<string> without = RunFight(false, 0);

        List<string> everyFrame = RunFight(true, 1);
        List<string> manyFrames = RunFight(true, 3);
        List<string> skippedFrames = RunFight(true, -7);

        Assert.That(without.Count, Is.GreaterThan(60));
        Assert.That(without, Has.Some.Contains("locked=True"), "the fight included a lock");
        Assert.That(without, Has.Some.StartsWith("attack"));
        Assert.That(everyFrame, Is.EqualTo(without));
        Assert.That(manyFrames, Is.EqualTo(without));
        Assert.That(skippedFrames, Is.EqualTo(without));
    }

    /// <summary>
    ///     The local player attacks the slime and is hit back; <paramref name="framesPerTick" /> is how often the
    ///     presenter draws per client tick, or with a negative value, once every that many ticks.
    /// </summary>
    private List<string> RunFight(bool isPresented, int framesPerTick)
    {
        ClientWorld world = CreateWorld();
        var log = new RecordingSink();
        var controller = new MovementController(world.Grid);
        var autoAttack = new AutoAttackState(world, controller, log, 0.05);
        var driver = new LocalPlayerDriver(controller, new MoveIntentProducer(), world, log, autoAttack);
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter? presenter = isPresented ? CreatePresenter(world) : null;
        var swing = new AttackTiming(
            TimeSpan.FromMilliseconds(940),
            TimeSpan.FromMilliseconds(470),
            TimeSpan.FromMilliseconds(470),
            TimeSpan.FromMilliseconds(235));

        autoAttack.Attack(Slime);
        for (uint tick = 1; tick <= 80; tick++)
        {
            if (tick == 20)
            {
                world.OnAttackStarted(new AttackStarted(Local, Slime, 20, swing));
            }
            else if (tick == 30)
            {
                world.OnDamage(new Damage(Local, Slime, CombatResult.Critical, 30, 30, 400));
                world.OnDamage(new Damage(Slime, Local, CombatResult.Hit, 7, 30, 0));
                world.OnCharacterHealth(new CharacterHealth(64, 71, 24, 24));
            }
            else if (tick == 50)
            {
                world.OnEntityDied(new EntityDied(Slime, Local, 50));
            }

            driver.Tick(tick);
            RemoteEntity slime = world.Remotes[Slime];
            log.Add(
                $"{tick} locked={controller.IsLocked} chasing={controller.IsChasing} active={autoAttack.IsActive}"
                + $" dead={world.IsLocalDead} hp={world.LocalHealth} target={world.Target.Value}"
                + $" slime={slime.IsDead},{slime.HealthPermille}");
            world.Advance(0.05f);
            int frames = framesPerTick >= 0 ? framesPerTick : tick % (uint)-framesPerTick == 0 ? 1 : 0;
            for (int frame = 0; frame < frames; frame++)
            {
                presenter?.Present(local, remotes, null);
            }
        }

        presenter?.Dispose();
        m_presenter = null;
        return log.Entries;
    }

    [Test]
    public void Despawn_OfAMonster_RemovesItsBar()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);
        presenter.Present(local, remotes, null);

        world.OnDespawn(new EntityDespawn(Slime, DespawnReason.Removed));

        Assert.That(presenter.TryGetHealthBar(Slime, out HealthBar? _), Is.False);
    }

    private sealed class RecordingSink : ICombatCommandSink, IMoveIntentSink
    {
        public List<string> Entries { get; } = new();

        public uint SendAttack(EntityId target)
        {
            Entries.Add($"attack {target.Value}");
            return (uint)Entries.Count;
        }

        public void SendCancel()
        {
            Entries.Add("cancel");
        }

        public void Send(MoveIntent intent)
        {
            Entries.Add($"move {intent.Sequence} {intent.DirectionX:R} {intent.DirectionZ:R}");
        }

        public void Add(string entry)
        {
            Entries.Add(entry);
        }
    }
}
}
