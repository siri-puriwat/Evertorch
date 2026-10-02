using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
///     An area skill's cast draws its telegraph (Gameplay Systems §9; Prototype Content §2): a ring of the skill's radius
///     at its caster's feet, unlit, from the cast's start for its cast time on the caster's interpolated timeline, ended
///     early by the caster's death alone. A skill with no area draws none.
/// </summary>
public sealed class AreaTelegraphPresenterTests
{
    private static readonly EntityId Local = new(100);
    private static readonly EntityId Monarch = new(300);
    private static readonly SkillDefinitionId QuakeSlam = new("skill.quake_slam");
    private static readonly SkillDefinitionId Strike = new("skill.strike");

    private readonly List<Object> m_created = new();
    private AreaTelegraphPresenter? m_presenter;

    [TearDown]
    public void TearDown()
    {
        m_presenter?.Dispose();
        m_presenter = null;
        foreach (GameObject ring in Rings())
        {
            Object.DestroyImmediate(ring);
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

    private static GameObject[] Rings()
    {
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(transform => transform.gameObject)
            .Where(gameObject => gameObject.name == AreaTelegraphPresenter.ObjectName)
            .ToArray();
    }

    private static GameObject[] ShownRings()
    {
        return Rings().Where(ring => ring.activeInHierarchy).ToArray();
    }

    private static ClientWorld CreateWorld()
    {
        NavigationCell[] cells = Enumerable.Repeat(NavigationCell.Level(NavigationSurface.Floor, 0f), 64).ToArray();
        var grid = new NavigationGrid(8, 8, 1f, 0f, 0f, 0.3f, 0.4f, cells);
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
        var world = new ClientWorld(grid, entered, 20);
        world.OnSpawn(
            new EntitySpawn(
                Monarch,
                EntityKind.Monster,
                "monster.slime_monarch",
                new WorldPosition(6.5f, 0f, 2.5f),
                new WorldDirection(-1f, 0f),
                EntityStateFlags.None,
                1000));
        return world;
    }

    private static ClientContent CreateContent()
    {
        var monarch = new ClientMonster(
            new MonsterDefinitionId("monster.slime_monarch"),
            "Slime Monarch",
            "monster_training_slime",
            "monster_training_slime_icon",
            isBoss: true);
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster> { { monarch.Id, monarch } },
            new Dictionary<ItemDefinitionId, ClientItem>(),
            new Dictionary<SkillDefinitionId, ClientSkill>
            {
                [QuakeSlam] = new(QuakeSlam, "Quake Slam", SkillTargetType.Self, "skill_quake_slam", areaRadius: 4f),
                [Strike] = new(Strike, "Strike", SkillTargetType.Enemy, "skill_strike")
            },
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
    }

    private Dictionary<EntityId, EntityView> CreateViews(out EntityView local)
    {
        var localObject = new GameObject("Local");
        localObject.transform.position = new Vector3(2.5f, 0f, 2.5f);
        m_created.Add(localObject);
        local = localObject.AddComponent<EntityView>();
        var monarchObject = new GameObject("Monster 300");
        monarchObject.transform.position = new Vector3(6.5f, 0f, 2.5f);
        m_created.Add(monarchObject);
        return new Dictionary<EntityId, EntityView> { { Monarch, monarchObject.AddComponent<EntityView>() } };
    }

    private AreaTelegraphPresenter CreatePresenter(ClientWorld world)
    {
        var overlay = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m_created.Add(overlay);
        m_presenter = new AreaTelegraphPresenter(world, CreateContent(), 0.05, overlay);
        return m_presenter;
    }

    // The slam's cast starts at tick 2 (0.1 s) and lasts 1,500 ms, to 1.6 s; the boss is drawn 0.1 s behind the
    // server time the world has reached, so at 0.05 s, 0.55 s, and 1.65 s here.
    [UnityTest]
    public IEnumerator Cast_OfAnAreaSkill_DrawsAnUnlitRingOfItsRadiusAtTheCastersFeet_ForItsCastTime()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        AreaTelegraphPresenter presenter = CreatePresenter(world);

        world.OnSkillCastStarted(new SkillCastStarted(Monarch, QuakeSlam, default, 2, 1500));
        world.Advance(0.15f);
        presenter.Present(local, remotes);
        int shownBeforeTheStart = ShownRings().Length;
        world.Advance(0.5f);
        presenter.Present(local, remotes);
        GameObject[] during = ShownRings();
        yield return null;
        Vector3 at = during.Length == 1 ? during[0].transform.position : default;
        Bounds bounds = during.Length == 1 ? during[0].GetComponent<MeshFilter>().sharedMesh.bounds : default;
        string shader = during.Length == 1 ? during[0].GetComponent<MeshRenderer>().sharedMaterial.shader.name : "";
        bool hasCollider = during.Length == 1 && during[0].GetComponent<Collider>() != null;
        world.Advance(1.1f);
        presenter.Present(local, remotes);
        yield return null;

        Assert.That(shownBeforeTheStart, Is.Zero, "drawn at 0.05 s, before the cast's start");
        Assert.That(during, Has.Length.EqualTo(1));
        Assert.That((at.x, at.z), Is.EqualTo((6.5f, 2.5f)), "at the boss's feet");
        Assert.That(at.y, Is.GreaterThan(0f).And.LessThan(0.1f), "just over the floor");
        Assert.That(bounds.extents.x, Is.EqualTo(4f).Within(1e-3f), "the slam's 4 m radius");
        Assert.That(shader, Is.EqualTo("Universal Render Pipeline/Unlit"), "readable in a dark map");
        Assert.That(hasCollider, Is.False, "a click never lands on it");
        Assert.That(presenter.Pending, Is.Zero, "over at the cast's end");
        Assert.That(Rings(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator Cast_OfASkillWithNoArea_DrawsNothing()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        AreaTelegraphPresenter presenter = CreatePresenter(world);

        world.OnSkillCastStarted(new SkillCastStarted(Monarch, Strike, Local, 2, 1500));
        world.Advance(0.6f);
        presenter.Present(local, remotes);
        yield return null;

        Assert.That(presenter.Pending, Is.Zero);
        Assert.That(Rings(), Is.Empty);
    }

    // No message tells of an interrupted cast, so another entity's death leaves the ring, and the caster's ends it.
    [UnityTest]
    public IEnumerator Cast_EndsEarlyAtItsCastersDeath_AndAtNoOneElses()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        AreaTelegraphPresenter presenter = CreatePresenter(world);

        world.OnSkillCastStarted(new SkillCastStarted(Monarch, QuakeSlam, default, 2, 1500));
        world.Advance(0.6f);
        presenter.Present(local, remotes);
        world.OnEntityDied(new EntityDied(Local, Monarch, 12));
        presenter.Present(local, remotes);
        int shownAfterThePlayersDeath = ShownRings().Length;
        world.OnEntityDied(new EntityDied(Monarch, Local, 13));
        presenter.Present(local, remotes);
        yield return null;

        Assert.That(shownAfterThePlayersDeath, Is.EqualTo(1));
        Assert.That(presenter.Pending, Is.Zero, "the boss's death ended it");
        Assert.That(Rings(), Is.Empty);
    }
}
}
