using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
/// The presenter turns the world's combat events into views, on the monster's interpolated timeline.
/// </summary>
public sealed class CombatPresenterTests
{
    private static readonly EntityId Local = new EntityId(100);
    private static readonly EntityId Slime = new EntityId(300);

    private readonly List<Object> m_created = new List<Object>();
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
        NavigationCell[] cells = new NavigationCell[8 * 8];
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
            1.5f);
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
    public void Despawn_OfAMonster_RemovesItsBar()
    {
        ClientWorld world = CreateWorld();
        Dictionary<EntityId, EntityView> remotes = CreateViews(out EntityView local);
        CombatPresenter presenter = CreatePresenter(world);
        presenter.Present(local, remotes, null);

        world.OnDespawn(new EntityDespawn(Slime, DespawnReason.Removed));

        Assert.That(presenter.TryGetHealthBar(Slime, out HealthBar? _), Is.False);
    }
}
}
