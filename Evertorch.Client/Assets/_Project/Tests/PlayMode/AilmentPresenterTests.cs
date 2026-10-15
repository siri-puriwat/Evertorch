using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The ailments as the bodies show them (Prototype Content §2): an icy-blue tint over a frozen body and a green one
///     over a poisoned body, the body's own colours back once they end, a player's trim and a placeholder untouched; a
///     held body's pose held; a word rising as each starts; a poison tick in green and "Resisted" in the miss colour,
///     neither flinching; and a cast bar or a telegraph ended by a hold or silence.
/// </summary>
public sealed class AilmentPresenterTests
{
    private const float TimeoutSeconds = 10f;

    private static readonly EntityId Local = new(100);
    private static readonly EntityId Slime = new(300);
    private static readonly SkillDefinitionId Quake = new("skill.quake_slam");
    private static readonly SkillDefinitionId Hush = new("skill.hush");
    private static readonly WorldPosition SlimeAt = new(4.5f, 0f, 2.5f);
    private static readonly ColorEqualityComparer SameColour = new(1e-4f);

    private readonly List<Object> m_created = new();
    private CombatPresenter? m_presenter;
    private AreaTelegraphPresenter? m_telegraphs;
    private EntityViewCatalog? m_catalog;

    [TearDown]
    public void TearDown()
    {
        m_presenter?.Dispose();
        m_presenter = null;
        m_telegraphs?.Dispose();
        m_telegraphs = null;
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
                SlimeAt,
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000));
        return world;
    }

    private static void Flag(ClientWorld world, uint tick, EntityStateFlags flags)
    {
        world.OnSnapshot(
            new EntitySnapshot(
                tick,
                0,
                new[] { new EntityState(Slime, SlimeAt, new WorldDirection(0f, 1f), 0f, 0f, 0f, flags) }));
    }

    private EntityView CreateView(string key, Color? tint = null, Color? bodyTint = null)
    {
        m_catalog ??= new EntityViewCatalog();
        var view = EntityView.Create(key, key, m_catalog, tint, 1f, bodyTint);
        m_created.Add(view.gameObject);
        return view;
    }

    private static IEnumerator WaitForBody(EntityView view)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (!view.HasBody && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static Color[] RendererColours(EntityView view)
    {
        var block = new MaterialPropertyBlock();
        return view.GetComponentsInChildren<Renderer>()
            .Select(renderer =>
            {
                renderer.GetPropertyBlock(block);
                return block.GetColor("_BaseColor");
            })
            .ToArray();
    }

    private CombatPresenter CreatePresenter(ClientWorld world)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m_created.Add(material);
        m_presenter = new CombatPresenter(world, 0.05, material);
        return m_presenter;
    }

    private Dictionary<EntityId, EntityView> CreateViews(out EntityView local)
    {
        var localObject = new GameObject("Local");
        var slimeObject = new GameObject("Slime");
        m_created.Add(localObject);
        m_created.Add(slimeObject);
        local = localObject.AddComponent<EntityView>();
        return new Dictionary<EntityId, EntityView> { { Slime, slimeObject.AddComponent<EntityView>() } };
    }

    private static Color ColourOf(string text)
    {
        return Object.FindObjectsByType<FloatingNumber>(FindObjectsSortMode.None)
            .Single(number => number.Text == text)
            .GetComponent<TextMeshPro>()
            .color;
    }

    [UnityTest]
    public IEnumerator AVariantBody_TakesThePoisonedTint_ThenTheFrozen_AndGetsItsOwnColourBack()
    {
        var violet = new Color(0.36f, 0.23f, 0.55f);
        EntityView view = CreateView("monster_forest_crawler", bodyTint: violet);
        view.ShowAilments(EntityStateFlags.Poisoned);
        yield return WaitForBody(view);

        Color[] poisoned = RendererColours(view);
        view.ShowAilments(EntityStateFlags.Poisoned | EntityStateFlags.Frozen);
        Color[] frozen = RendererColours(view);
        view.ShowAilments(EntityStateFlags.Moving);
        Color[] restored = RendererColours(view);

        Assert.That(poisoned, Is.Not.Empty);
        Assert.That(poisoned, Is.All.EqualTo(EntityView.PoisonedTint).Using(SameColour), "laid as the body attached");
        Assert.That(frozen, Is.All.EqualTo(EntityView.FrozenTint).Using(SameColour), "frozen wins");
        Assert.That(restored, Is.All.EqualTo(violet).Using(SameColour), "its own tint again");
        Assert.That(view.AilmentTint, Is.Null);
    }

    [UnityTest]
    public IEnumerator APlayersBody_KeepsItsTrimColour_UnderTheFrozenTint_AndAPlaceholderTakesNone()
    {
        var identity = new Color(0.9f, 0.1f, 0.2f);
        EntityView player = CreateView("character_adventurer", identity);
        EntityView placeholder = CreateView("no_such_body");
        yield return WaitForBody(player);
        yield return WaitForBody(placeholder);
        EntityBody body = player.GetComponentInChildren<EntityBody>();
        EntityBody.TrimSlot trim = body.TrimSlots[0];
        Renderer trimmed = trim.Renderer!;
        var block = new MaterialPropertyBlock();

        player.ShowAilments(EntityStateFlags.Frozen);
        placeholder.ShowAilments(EntityStateFlags.Frozen);
        Color[] frozen = RendererColours(player);
        trimmed.GetPropertyBlock(block, trim.MaterialIndex);
        Color trimWhileFrozen = block.GetColor("_BaseColor");
        placeholder.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
        bool isPlaceholderUntouched = block.isEmpty;
        player.ShowAilments(EntityStateFlags.None);
        trimmed.GetPropertyBlock(block);
        bool isRendererBlockCleared = block.isEmpty;
        trimmed.GetPropertyBlock(block, trim.MaterialIndex);

        Assert.That(frozen, Is.All.EqualTo(EntityView.FrozenTint).Using(SameColour));
        Assert.That(trimWhileFrozen, Is.EqualTo(identity).Using(SameColour), "the trim's own block keeps its colour");
        Assert.That(isPlaceholderUntouched, Is.True);
        Assert.That(isRendererBlockCleared, Is.True, "the ailment's tint is gone");
        Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(identity).Using(SameColour));
    }

    [UnityTest]
    public IEnumerator AHeldRiggedBody_HoldsItsPose_TillItsHoldEnds()
    {
        ClientWorld world = CreateWorld();
        EntityView slime = CreateView("monster_training_slime");
        yield return WaitForBody(slime);
        Assert.That(slime.HasClips, Is.True, "the slime's body is rigged");
        EntityView local = new GameObject("Local").AddComponent<EntityView>();
        m_created.Add(local.gameObject);
        CombatPresenter presenter = CreatePresenter(world);
        var remotes = new Dictionary<EntityId, EntityView> { [Slime] = slime };
        presenter.Present(local, remotes, null);
        yield return null;

        Flag(world, 1, EntityStateFlags.Asleep);
        presenter.Present(local, remotes, null);
        double heldAt = slime.BodyAnimator!.CurrentTime;
        string heldClip = slime.BodyAnimator.CurrentClip;
        yield return new WaitForSecondsRealtime(0.2f);
        presenter.Present(local, remotes, null);
        double stillAt = slime.BodyAnimator.CurrentTime;
        Flag(world, 2, EntityStateFlags.None);
        yield return new WaitForSecondsRealtime(0.2f);
        presenter.Present(local, remotes, null);

        Assert.That(heldClip, Is.Not.Empty);
        Assert.That(stillAt, Is.EqualTo(heldAt), "asleep, the clip stands still");
        Assert.That(slime.BodyAnimator.CurrentTime, Is.Not.EqualTo(heldAt), "awake, it moves again");
    }

    [Test]
    public void APoisonTick_IsAGreenNumberWithoutAFlinch_AndResistedIsInTheMissColour()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);
        presenter.Present(local, remotes, null);
        presenter.TryGetHealthBar(Slime, out HealthBar? bar);

        world.OnDamage(new Damage(default, Slime, CombatResult.Poison, 12, 2, 940));
        world.OnDamage(new Damage(Local, Slime, CombatResult.Miss, 0, 2, 940));
        world.OnSkillResolved(new SkillResolved(Local, Slime, Hush, SkillOutcome.Resisted, 0, 2, 940));
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);

        Assert.That(presenter.NumbersShown, Is.EqualTo(3));
        Assert.That(ColourOf("12").g, Is.GreaterThan(ColourOf("12").r), "green");
        Assert.That(ColourOf("Resisted"), Is.EqualTo(ColourOf("Miss")));
        Assert.That(
            presenter.Timeline.TryGetLastHit(Slime, world.ServerTime.Now, world.RemoteRenderTime, out _),
            Is.False,
            "no flinch");
        Assert.That(bar!.ShownRatio, Is.EqualTo(0.94f).Within(1e-4f), "the tick's HP");
    }

    [Test]
    public void AnAilmentsStart_RaisesItsWordOnTheBodysTimeline_AndMovesNoBar()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);
        presenter.Present(local, remotes, null);
        presenter.TryGetHealthBar(Slime, out HealthBar? bar);

        Flag(world, 2, EntityStateFlags.Frozen | EntityStateFlags.Poisoned);
        presenter.Present(local, remotes, null);
        int shownAtOnce = presenter.NumbersShown;
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);
        Flag(world, 3, EntityStateFlags.Frozen | EntityStateFlags.Poisoned);
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);

        Assert.That(shownAtOnce, Is.Zero, "drawn at its snapshot's moment");
        Assert.That(
            Object.FindObjectsByType<FloatingNumber>(FindObjectsSortMode.None).Select(number => number.Text),
            Is.EquivalentTo(new[] { "Poisoned!", "Frozen!" }));
        Assert.That(presenter.NumbersShown, Is.EqualTo(2), "an ailment kept rises no word again");
        Assert.That(bar!.ShownRatio, Is.EqualTo(1f));
    }

    [Test]
    public void AHoldOrSilence_EndsTheCastersBar_AndAPoisonDoesNot()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);

        world.OnSkillCastStarted(new SkillCastStarted(Slime, Quake, Local, 2, 2000));
        world.Advance(0.3f);
        presenter.Present(local, remotes, null);
        presenter.TryGetCastBar(Slime, out CastBar? cast);
        bool isShown = cast!.IsShown;
        Flag(world, 4, EntityStateFlags.Poisoned);
        presenter.Present(local, remotes, null);
        bool isShownPoisoned = cast.IsShown;
        Flag(world, 5, EntityStateFlags.Poisoned | EntityStateFlags.Silenced);
        presenter.Present(local, remotes, null);

        Assert.That(isShown, Is.True);
        Assert.That(isShownPoisoned, Is.True);
        Assert.That(cast.IsShown, Is.False, "silenced");
    }

    [Test]
    public void AHold_EndsTheCastersTelegraph()
    {
        ClientWorld world = CreateWorld();
        var content = new ClientContent(
            "test",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster>(),
            new Dictionary<ItemDefinitionId, ClientItem>(),
            new Dictionary<SkillDefinitionId, ClientSkill>
            {
                [Quake] = new(Quake, "Quake Slam", SkillTargetType.Self, "skill_quake_slam", areaRadius: 4f)
            },
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m_created.Add(material);
        m_telegraphs = new AreaTelegraphPresenter(world, content, 0.05, material);

        world.OnSkillCastStarted(new SkillCastStarted(Slime, Quake, default, 2, 2000));
        int begun = m_telegraphs.Pending;
        Flag(world, 4, EntityStateFlags.Stunned);

        Assert.That(begun, Is.EqualTo(1));
        Assert.That(m_telegraphs.Pending, Is.Zero, "stunned");
    }
}
}
