using System.Collections;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The names under the feet (Prototype Content §2): every player's, the local player's included, and every NPC's,
///     never a monster's, with rich text off.
/// </summary>
public sealed class NamePlatePresenterTests
{
    private static readonly EntityId Local = new(100);
    private static readonly EntityId Other = new(200);
    private static readonly EntityId Quartermaster = new(201);
    private static readonly EntityId Slime = new(300);

    private readonly List<Object> m_created = new();
    private NamePlatePresenter? m_presenter;

    [TearDown]
    public void TearDown()
    {
        m_presenter?.Dispose();
        m_presenter = null;
        foreach (NamePlate plate in
                 Object.FindObjectsByType<NamePlate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(plate.gameObject);
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
        Spawn(world, Other, EntityKind.Player, "job.vanguard", 0, "Anna");
        Spawn(world, Quartermaster, EntityKind.Npc, "npc.quartermaster", 0, string.Empty);
        Spawn(world, Slime, EntityKind.Monster, "monster.training_slime", 1000, string.Empty);
        return world;
    }

    private static void Spawn(
        ClientWorld world,
        EntityId entity,
        EntityKind kind,
        string definition,
        ushort healthPermille,
        string name)
    {
        world.OnSpawn(
            new EntitySpawn(
                entity,
                kind,
                definition,
                new WorldPosition(4.5f, 0f, 2.5f),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                healthPermille,
                string.Empty,
                name));
    }

    private EntityView CreateView(string objectName, Vector3 position)
    {
        var root = new GameObject(objectName);
        root.transform.position = position;
        m_created.Add(root);
        return root.AddComponent<EntityView>();
    }

    private Dictionary<EntityId, EntityView> CreateRemoteViews()
    {
        return new Dictionary<EntityId, EntityView>
        {
            { Other, CreateView("Other", new Vector3(4.5f, 0f, 2.5f)) },
            { Quartermaster, CreateView("Quartermaster", new Vector3(5.5f, 0f, 3.5f)) },
            { Slime, CreateView("Slime", new Vector3(6.5f, 0f, 2.5f)) }
        };
    }

    [Test]
    public void Despawn_RemovesThePlate()
    {
        ClientWorld world = CreateWorld();
        m_presenter = new NamePlatePresenter(world, null, "Bobby");
        Dictionary<EntityId, EntityView> remotes = CreateRemoteViews();
        m_presenter.Present(null, remotes, null);

        world.OnDespawn(new EntityDespawn(Other, DespawnReason.OutOfRange));
        remotes.Remove(Other);
        m_presenter.Present(null, remotes, null);

        Assert.That(m_presenter.TryGetPlate(Other, out _), Is.False);
        Assert.That(m_presenter.TryGetPlate(Quartermaster, out _), Is.True);
    }

    // Unity destroys at the end of the frame.
    [UnityTest]
    public IEnumerator Dispose_DestroysEveryPlate()
    {
        ClientWorld world = CreateWorld();
        m_presenter = new NamePlatePresenter(world, null, "Bobby");
        m_presenter.Present(CreateView("Local", Vector3.zero), CreateRemoteViews(), null);

        m_presenter.Dispose();
        m_presenter = null;
        yield return null;

        Assert.That(
            Object.FindObjectsByType<NamePlate>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Length,
            Is.Zero);
    }

    [Test]
    public void Font_DrawsTheMiddleDotOfTheTargetFramesPlayerLabel()
    {
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;

        Assert.That(TargetFrame.PlayerLabel("Anna", "Vanguard"), Is.EqualTo("Anna \u00B7 Vanguard"));
        Assert.That(TargetFrame.PlayerLabel(string.Empty, "Vanguard"), Is.EqualTo("Vanguard"));
        Assert.That(font.HasCharacter('\u00B7'), Is.True, "the middle dot is in the font");
    }

    [Test]
    public void Present_NamesThePlayersAndTheNpc_UnderTheirFeet_AndNeverTheMonster()
    {
        ClientWorld world = CreateWorld();
        m_presenter = new NamePlatePresenter(world, null, "Bobby");
        EntityView local = CreateView("Local", new Vector3(2.5f, 0f, 2.5f));

        m_presenter.Present(local, CreateRemoteViews(), null);

        Assert.That(m_presenter.TryGetPlate(Other, out NamePlate? other), Is.True);
        Assert.That(other!.Text, Is.EqualTo("Anna"));
        Assert.That(other.transform.position.y, Is.LessThan(0f), "under the feet");
        Assert.That(other.IsRichText, Is.False);
        Assert.That(m_presenter.TryGetPlate(Quartermaster, out NamePlate? npc), Is.True);
        Assert.That(npc!.Text, Is.EqualTo("npc.quartermaster"), "without content the definition names it");
        Assert.That(m_presenter.TryGetPlate(Slime, out _), Is.False, "a monster shows no name");
        Assert.That(m_presenter.LocalPlate!.Text, Is.EqualTo("Bobby"));
        Assert.That(m_presenter.LocalPlate.transform.position.x, Is.EqualTo(2.5f).Within(0.001f));
    }

    [Test]
    public void Present_WithACamera_DropsThePlateDownTheScreen_AndFacesTheCamera()
    {
        ClientWorld world = CreateWorld();
        m_presenter = new NamePlatePresenter(world, null, "Bobby");
        var cameraObject = new GameObject("Camera");
        m_created.Add(cameraObject);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(4.5f, 8f, -4f), Quaternion.Euler(50f, 0f, 0f));

        m_presenter.Present(null, CreateRemoteViews(), camera);

        Assert.That(m_presenter.TryGetPlate(Other, out NamePlate? plate), Is.True);
        Vector3 feet = new(4.5f, 0f, 2.5f);
        Vector3 viewport = camera.WorldToViewportPoint(plate!.transform.position);
        Assert.That(viewport.y, Is.LessThan(camera.WorldToViewportPoint(feet).y), "below the feet on screen");
        Assert.That(Quaternion.Angle(plate.transform.rotation, camera.transform.rotation), Is.LessThan(0.01f));
    }
}
}
