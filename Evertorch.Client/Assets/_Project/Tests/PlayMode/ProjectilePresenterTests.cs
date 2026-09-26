using System;
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
///     A monster whose content names a projectile flies one at its target for each attack and cast, on the monster's
///     interpolated timeline (Gameplay Systems §8); one that names none flies nothing.
/// </summary>
public sealed class ProjectilePresenterTests
{
    private const string AbsentKey = "projectile_absent";

    private static readonly EntityId Local = new(100);
    private static readonly EntityId Wisp = new(300);
    private static readonly EntityId Slime = new(301);
    private static readonly EntityId OtherWisp = new(302);
    private static readonly SkillDefinitionId SparkBolt = new("skill.spark_bolt");

    // Interval 1.8 s, impact 0.9 s after the start.
    private static readonly AttackTiming Timing = new(
        TimeSpan.FromMilliseconds(1800),
        TimeSpan.FromMilliseconds(600),
        TimeSpan.FromMilliseconds(900),
        TimeSpan.FromMilliseconds(900));

    private readonly List<Object> m_created = new();
    private readonly EntityViewCatalog m_catalog = new();
    private ProjectilePresenter? m_presenter;

    [TearDown]
    public void TearDown()
    {
        m_presenter?.Dispose();
        m_presenter = null;
        foreach (GameObject projectile in Projectiles())
        {
            Object.DestroyImmediate(projectile);
        }

        for (int index = m_created.Count - 1; index >= 0; index--)
        {
            if (m_created[index] != null)
            {
                Object.DestroyImmediate(m_created[index]);
            }
        }

        m_created.Clear();
        m_catalog.Dispose();
    }

    private static GameObject[] Projectiles()
    {
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(transform => transform.gameObject)
            .Where(gameObject => gameObject.name.StartsWith("Projectile ", StringComparison.Ordinal))
            .ToArray();
    }

    private static ClientWorld CreateWorld()
    {
        NavigationCell[] cells = Enumerable.Repeat(NavigationCell.Level(NavigationSurface.Floor, 0f), 64).ToArray();
        var grid = new NavigationGrid(8, 8, 1f, 0f, 0f, 0.3f, 0.4f, cells);
        var entered = new WorldEntered(
            new MapDefinitionId("map.training_field"),
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
        foreach ((EntityId entity, string definition) in new[]
                 {
                     (Wisp, "monster.spark_wisp"), (Slime, "monster.training_slime"),
                     (OtherWisp, "monster.spark_wisp")
                 })
        {
            world.OnSpawn(
                new EntitySpawn(
                    entity,
                    EntityKind.Monster,
                    definition,
                    new WorldPosition(6.5f, 0f, 2.5f),
                    new WorldDirection(-1f, 0f),
                    EntityStateFlags.None,
                    1000));
        }

        return world;
    }

    private static ClientContent CreateContent()
    {
        var wisp = new ClientMonster(
            new MonsterDefinitionId("monster.spark_wisp"),
            "Spark Wisp",
            "monster_spark_wisp",
            "monster_spark_wisp_icon",
            AbsentKey);
        var slime = new ClientMonster(
            new MonsterDefinitionId("monster.training_slime"),
            "Training Slime",
            "monster_training_slime",
            "monster_training_slime_icon");
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster> { { wisp.Id, wisp }, { slime.Id, slime } },
            new Dictionary<ItemDefinitionId, ClientItem>(),
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
    }

    private Dictionary<EntityId, EntityView> CreateViews(out EntityView local)
    {
        var localObject = new GameObject("Local");
        localObject.transform.position = new Vector3(2.5f, 0f, 2.5f);
        m_created.Add(localObject);
        local = localObject.AddComponent<EntityView>();
        var views = new Dictionary<EntityId, EntityView>();
        foreach (EntityId monster in new[] { Wisp, Slime, OtherWisp })
        {
            var monsterObject = new GameObject($"Monster {monster.Value}");
            monsterObject.transform.position = new Vector3(6.5f, 0f, 2.5f);
            m_created.Add(monsterObject);
            views.Add(monster, monsterObject.AddComponent<EntityView>());
        }

        return views;
    }

    private ProjectilePresenter CreatePresenter(ClientWorld world)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m_created.Add(material);
        m_presenter = new ProjectilePresenter(world, CreateContent(), m_catalog, 0.05, material);
        return m_presenter;
    }

    // The attack starts at tick 2 (0.1 s) and lands 0.9 s later, at 1.0 s: the projectile flies from 0.6 s. The
    // monsters are drawn 0.1 s behind the server time the world has reached.
    [UnityTest]
    public IEnumerator Attack_ByAMonsterThatNamesAProjectile_FliesAPlainSphereOverItsLastFourTenths()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        ProjectilePresenter presenter = CreatePresenter(world);

        world.OnAttackStarted(new AttackStarted(Wisp, Local, 2, Timing));
        world.OnAttackStarted(new AttackStarted(Slime, Local, 2, Timing));
        world.Advance(0.6f);
        presenter.Present(local, remotes);
        int launchedEarly = presenter.Launched;
        world.Advance(0.3f);
        presenter.Present(local, remotes);
        GameObject[] halfway = Projectiles();
        Vector3 position = halfway.Length == 1 ? halfway[0].transform.position : default;
        yield return null;
        bool hasCollider = halfway.Length == 1 && halfway[0].GetComponent<Collider>() != null;
        world.Advance(0.2f);
        presenter.Present(local, remotes);
        yield return null;

        Assert.That(launchedEarly, Is.Zero, "0.5 s is before the launch");
        Assert.That(halfway, Has.Length.EqualTo(1), "only the wisp names a projectile");
        Assert.That(position.x, Is.EqualTo(4.5f).Within(1e-3f), "halfway from 6.5 to 2.5");
        Assert.That(position.y, Is.EqualTo(1f).Within(1e-3f), "1 m over both");
        Assert.That(hasCollider, Is.False, "a click never lands on it");
        Assert.That(presenter.Pending, Is.Zero, "it landed at the impact");
        Assert.That(Projectiles(), Is.Empty);
    }

    // A cast from tick 2 of 1,500 ms resolves at 1.6 s. Its wisp dies at tick 20 (1.0 s), before that, which ends
    // the flight. The other wisp's attack lands at 1.0 s, the moment the local player dies, and still flies: it is
    // the blow that killed.
    [Test]
    public void Cast_FliesToItsResolution_AndADeathBeforeTheImpactEndsIt()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        ProjectilePresenter presenter = CreatePresenter(world);

        world.OnSkillCastStarted(new SkillCastStarted(Wisp, SparkBolt, Local, 2, 1500));
        world.OnSkillCastStarted(new SkillCastStarted(Wisp, SparkBolt, default, 2, 1500));
        int pendingCasts = presenter.Pending;
        world.OnEntityDied(new EntityDied(Wisp, Local, 20));
        int pendingAfterDeath = presenter.Pending;
        world.OnAttackStarted(new AttackStarted(OtherWisp, Local, 2, Timing));
        world.OnEntityDied(new EntityDied(Local, OtherWisp, 20));
        world.Advance(1.0f);
        presenter.Present(local, remotes);

        Assert.That(pendingCasts, Is.EqualTo(1), "a cast at nobody the client knows flies nothing");
        Assert.That(pendingAfterDeath, Is.Zero, "the caster died first");
        Assert.That(presenter.Pending, Is.EqualTo(1), "the killing blow still lands");
        Assert.That(presenter.Launched, Is.EqualTo(1), "drawn at 0.9 s, between its launch and its impact");
    }
}
}
