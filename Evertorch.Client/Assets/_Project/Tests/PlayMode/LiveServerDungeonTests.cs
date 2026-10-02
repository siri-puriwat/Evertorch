using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 13 "A dungeon and its boss" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>DungeonAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of what the real
///     client draws: the grotto's look, the variant bodies, the target frame, the slam's telegraph, and the boss's
///     words in the chat log.
/// </summary>
public sealed class LiveServerDungeonTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string ClientName = "LiveDungeonOne";
    private const string FieldScene = "11_TrainingField";
    private const string GrottoScene = "12_UmbralGrotto";
    private const string Unlit = "Universal Render Pipeline/Unlit";
    private const float StartTimeoutSeconds = 30f;
    private const float StepTimeoutSeconds = 20f;
    private const int TestTimeoutMs = 300_000;

    // Where the field's way to the grotto begins, by its east wall; the portal before the east gate; and a step into
    // the grotto's entrance hall.
    private static readonly WorldPosition FieldEastSide = new(20.5f, 0f, -8f);
    private static readonly WorldPosition FieldEastPortal = new(22.2f, 0f, -8f);
    private static readonly WorldPosition EntranceHall = new(-24f, 0f, -22f);

    // The crawler hall's south end, from where the pack in its middle is in view.
    private static readonly WorldPosition CrawlerHall = new(-22f, 0f, 4f);

    // The Grotto Crawler's tint in the content, "#4B6B3C".
    private static readonly Color CrawlerTint = new Color32(0x4B, 0x6B, 0x3C, 0xFF);

    private LiveDatabase? m_database;
    private LiveServer? m_server;
    private GameObject? m_client;
    private InputActionAsset? m_actions;

    [UnityTearDown]
    public IEnumerator StopEverything()
    {
        if (m_client != null)
        {
            Object.Destroy(m_client);
            m_client = null;
        }

        m_server?.Dispose();
        m_server = null;
        m_database?.Dispose();
        m_database = null;
        if (m_actions != null)
        {
            Object.Destroy(m_actions);
            m_actions = null;
        }

        // As in LiveServerArtTests: only a scene the client loaded is unloaded, never the test runner's own.
        Scene loaded = SceneManager.GetActiveScene();
        Scene empty = SceneManager.CreateScene($"Empty {Guid.NewGuid():N}");
        SceneManager.SetActiveScene(empty);
        if (loaded.isLoaded
            && (MapSceneResolver.IsMapScene(loaded.name) || loaded.name == BootstrapRedirect.MainMenuScene))
        {
            yield return SceneManager.UnloadSceneAsync(loaded);
        }
    }

    // The real client crosses from the ground to the field, walks to the field's east side, and crosses into the
    // grotto (Gameplay Systems §4.2): the grotto's dark scene, the graybox in the scene's colours, and the bars and
    // rings over the map drawn unlit (Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Crossing_TheRealClientWalksOverTheFieldIntoTheDarkGrotto()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, ClientName);
        ClientWorld ground = client.World!;

        Assert.That(
            client.Controller!.TryMoveTo(ground.Predictor.Position, new WorldPosition(22.2f, 0f, 0f)),
            Is.True,
            "a way into the ground's portal");
        yield return WaitUntil(
            () => client.World != null && client.World != ground && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        ClientWorld field = client.World!;
        Assert.That(field.Map.Value, Is.EqualTo("map.training_field"), $"{client.Status} {server.JoinOutput()}");
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(FieldScene));

        yield return WalkTo(client, field, FieldEastSide, "by the east wall");
        Assert.That(
            Object.FindObjectsByType<HealthBar>(FindObjectsSortMode.None),
            Is.Not.Empty,
            "the field's monsters have bars");
        Assert.That(OverlayShaders(), Is.All.EqualTo(Unlit), "the bars and rings over the field");

        Assert.That(
            client.Controller!.TryMoveTo(field.Predictor.Position, FieldEastPortal),
            Is.True,
            "a way into the field's east portal");
        yield return WaitUntil(
            () => client.World != null && client.World != field && client.World.Inventory.IsCurrent,
            StepTimeoutSeconds);
        ClientWorld grotto = client.World!;
        Assert.That(grotto.Map.Value, Is.EqualTo("map.umbral_grotto"), $"{client.Status} {server.JoinOutput()}");
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GrottoScene));
        Assert.That(RenderSettings.fog, Is.True, "the grotto's fog");
        MapLook look = Object.FindFirstObjectByType<MapLook>();
        GrayboxMap map = Object.FindFirstObjectByType<GrayboxMap>();
        Assert.That(look, Is.Not.Null, "the grotto's look");
        Assert.That(
            map.GetComponent<MeshRenderer>().sharedMaterials.Select(surface => surface.color),
            Is.EqualTo(look.Palette).Using(new ColorEqualityComparer(1e-4f)),
            "the graybox in the grotto's colours");

        yield return WalkTo(client, grotto, EntranceHall, "in the entrance hall");

        // The Grotto Crawler is the forest crawler's body at 1.2 times its size, in the content's tint.
        yield return WalkTo(client, grotto, CrawlerHall, "in the crawler hall");
        EntityView? crawler = null;
        yield return WaitUntil(
            () => (crawler = CrawlerView(client)) != null && crawler.HasBody,
            StepTimeoutSeconds);
        Assert.That(crawler, Is.Not.Null, "a Grotto Crawler in view");
        Assert.That((crawler!.Key, crawler.Scale), Is.EqualTo(("monster_forest_crawler", 1.2f)));
        var block = new MaterialPropertyBlock();
        foreach (Renderer part in crawler.GetComponentsInChildren<Renderer>())
        {
            part.GetPropertyBlock(block);
            Assert.That(
                block.GetColor("_BaseColor"),
                Is.EqualTo(CrawlerTint).Using(new ColorEqualityComparer(1e-4f)),
                part.name);
        }

        Assert.That(client.Connection!.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    private static EntityView? CrawlerView(GameClient client)
    {
        ClientWorld? world = client.World;
        RemoteEntity? remote = world?.Remotes.Values
            .FirstOrDefault(entity => entity.DefinitionId == "monster.grotto_crawler");
        return remote != null && client.RemoteViews.TryGetValue(remote.Entity, out EntityView? view) ? view : null;
    }

    // The renderers of every health bar, the target ring, and the move marker.
    private static string[] OverlayShaders()
    {
        IEnumerable<Renderer> bars = Object
            .FindObjectsByType<HealthBar>(FindObjectsSortMode.None)
            .SelectMany(bar => bar.GetComponentsInChildren<Renderer>(true));
        IEnumerable<Renderer> rings = Object
            .FindObjectsByType<TargetMarker>(FindObjectsSortMode.None)
            .Select(marker => marker.GetComponent<Renderer>())
            .Concat(
                Object.FindObjectsByType<MoveMarker>(FindObjectsSortMode.None)
                    .Select(marker => marker.GetComponent<Renderer>()));
        return bars.Concat(rings).Select(renderer => renderer.sharedMaterial.shader.name).ToArray();
    }

    private static IEnumerator WalkTo(GameClient client, ClientWorld world, WorldPosition destination, string where)
    {
        Assert.That(client.Controller!.TryMoveTo(world.Predictor.Position, destination), Is.True, $"a way {where}");
        yield return WaitUntil(
            () => client.Controller != null && !client.Controller.HasPath && world.Predictor.PendingCount == 0,
            StepTimeoutSeconds);
        WorldPosition at = world.Predictor.Position;
        Assert.That(
            Math.Abs(at.X - destination.X) + Math.Abs(at.Z - destination.Z),
            Is.LessThan(0.5f),
            $"{where}, at {at}");
    }

    private static string RequirePrerequisites()
    {
        if (!LiveServer.IsBuilt())
        {
            Assert.Inconclusive(LiveServer.MissingPrerequisites);
        }

        string? mismatch = LiveServer.ContentMismatch();
        if (mismatch != null)
        {
            Assert.Fail(mismatch);
        }

        string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
        Assert.That(File.Exists(actionsPath), Is.True, actionsPath);
        return actionsPath;
    }

    // Creates the named character and enters the world with it.
    private static IEnumerator EnterByName(GameClient client, string name)
    {
        yield return LiveSignIn.PressConnect(client);
        yield return WaitUntil(() => client.Connection != null, StartTimeoutSeconds);
        ClientConnection connection = client.Connection!;
        bool hasList = false;
        connection.CharactersChanged += () => hasList = true;
        yield return WaitUntil(() => hasList, StartTimeoutSeconds);
        Assert.That(hasList, Is.True, $"no character list: {client.Status}");
        client.CreateCharacter(name);
        yield return WaitUntil(() => connection.Characters.Any(entry => entry.Name == name), StartTimeoutSeconds);
        client.EnterWorld(connection.Characters.Single(entry => entry.Name == name).Character);
        yield return WaitUntil(() => client.World?.Inventory.IsCurrent == true, StartTimeoutSeconds);
        Assert.That(client.World?.Inventory.IsCurrent, Is.True, client.Status);
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private IEnumerator StartDatabaseAndServer()
    {
        Task<LiveDatabase> starting = LiveDatabase.StartAsync();
        yield return new WaitUntil(() => starting.IsCompleted);
        Assert.That(starting.IsFaulted, Is.False, starting.Exception?.GetBaseException().Message);
        m_database = starting.Result;

        LiveServer server = m_server = new LiveServer();
        server.Start(m_database, "--World:RandomSeed=13");
        Assert.That(server.IsTiedToEditor || !KillOnCloseJob.IsSupported, Is.True, "the server ends with the editor");
        yield return WaitUntil(() => server.TryReadListeningPort(out int _), StartTimeoutSeconds);
    }

    private GameClient CreateClient(string actionsPath)
    {
        m_actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
        m_client = new GameObject("TestGameClient");
        m_client.SetActive(false);
        GameClient client = m_client.AddComponent<GameClient>();

        // The bootstrap scene assigns the actions in the inspector; this test builds the client itself.
        typeof(GameClient)
            .GetField("m_inputActions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, m_actions);
        LiveSignIn.Prepare(client, m_server!);
        m_client.SetActive(true);
        return client;
    }
}
}
