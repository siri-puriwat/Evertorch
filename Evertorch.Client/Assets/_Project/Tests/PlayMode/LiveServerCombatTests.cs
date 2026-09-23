using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The whole client against the real server process (Milestone 3 verification "the player can kill the training
///     slime and see a slime-gel drop"): <see cref="GameClient" /> with the project's input actions, driven only by
///     simulated keys and buttons: Tab to target, the gamepad's West button to attack, R to respawn.
/// </summary>
public sealed class LiveServerCombatTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string SlimeGel = "item.material.slime_gel";
    private const string GelModel = "pickup_slime_gel";
    private const float StartTimeoutSeconds = 30f;
    private const float FightTimeoutSeconds = 180f;

    private readonly List<ItemDropped> m_dropped = new();
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

        // Leave the next test an empty scene rather than the map, whose camera and ground would still be there.
        Scene loaded = SceneManager.GetActiveScene();
        Scene empty = SceneManager.CreateScene($"Empty {Guid.NewGuid():N}");
        SceneManager.SetActiveScene(empty);
        if (loaded.IsValid() && loaded.isLoaded && loaded != empty)
        {
            yield return SceneManager.UnloadSceneAsync(loaded);
        }
    }

    [UnityTest]
    public IEnumerator Player_FightsSlimesWithKeysAndButtons_UntilOneDropsGelThatIsDrawnFromItsModel()
    {
        string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
        if (!LiveServer.IsBuilt() || !File.Exists(actionsPath))
        {
            Assert.Inconclusive(LiveServer.MissingPrerequisites);
        }

        Task<LiveDatabase> starting = LiveDatabase.StartAsync();
        yield return new WaitUntil(() => starting.IsCompleted);
        Assert.That(starting.IsFaulted, Is.False, starting.Exception?.GetBaseException().Message);
        LiveDatabase database = m_database = starting.Result;

        LiveServer server = m_server = new LiveServer();
        server.Start(database, "--World:RandomSeed=11");
        yield return WaitUntil(() => server.TryReadListeningPort(out int _), StartTimeoutSeconds);
        Assert.That(server.TryReadListeningPort(out int port), Is.True, $"server output: {server.JoinOutput()}");

        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        GameClient client = CreateClient(port, actionsPath);
        yield return WaitUntil(() => client.World != null && client.Combat != null, StartTimeoutSeconds);
        Assert.That(client.World, Is.Not.Null, $"{client.Status} server output: {server.JoinOutput()}");
        ClientWorld world = client.World!;
        world.ItemDroppedReceived += m_dropped.Add;

        int kills = 0;
        int respawns = 0;
        world.EntityDiedReceived += died =>
        {
            if (died.Entity != world.LocalEntity)
            {
                kills++;
            }
        };
        EntityId attacking = default;
        float deadline = Time.realtimeSinceStartup + FightTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline && FindGelView(client) == null)
        {
            if (world.IsLocalDead)
            {
                yield return Tap(keyboard.rKey);
                respawns++;
                yield return WaitUntil(() => !world.IsLocalDead, 5f);
                attacking = default;
            }
            else if (world.Target == default)
            {
                yield return Tap(keyboard.tabKey);
                yield return WaitUntil(() => world.Target != default, 2f);
            }
            else if (world.Target != attacking)
            {
                attacking = world.Target;
                yield return Tap(gamepad.buttonWest);
            }
            else
            {
                yield return null;
            }
        }

        EntityView? gel = FindGelView(client);
        Assert.That(
            gel,
            Is.Not.Null,
            $"kills {kills}, respawns {respawns}, drops {m_dropped.Count}, status {client.Status}");
        Assert.That(gel!.HasBody, Is.True);
        Assert.That(gel.IsPlaceholder, Is.False, "the body is the pickup_slime_gel prefab, not the placeholder");
        Assert.That(m_dropped.Any(dropped => dropped.ItemId == SlimeGel), Is.True);
        Assert.That(kills, Is.GreaterThanOrEqualTo(1));
        Debug.Log($"Live combat: {kills} kills, {respawns} respawns, {m_dropped.Count} drops announced");
    }

    private static EntityView? FindGelView(GameClient client)
    {
        ClientWorld? world = client.World;
        if (world == null)
        {
            return null;
        }

        foreach (KeyValuePair<EntityId, EntityView> pair in client.RemoteViews)
        {
            if (world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote)
                && remote.Kind == EntityKind.ItemDrop
                && remote.DefinitionId == SlimeGel
                && pair.Value.Key == GelModel
                && pair.Value.HasBody)
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private GameClient CreateClient(int port, string actionsPath)
    {
        m_actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
        m_client = new GameObject("TestGameClient");
        m_client.SetActive(false);
        GameClient client = m_client.AddComponent<GameClient>();

        // The bootstrap scene assigns the actions in the inspector; this test builds the client itself.
        typeof(GameClient)
            .GetField("m_inputActions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, m_actions);
        client.Host = "127.0.0.1";
        client.Port = port;
        m_client.SetActive(true);
        return client;
    }

    private IEnumerator Tap(ButtonControl button)
    {
        Press(button);
        yield return null;
        Release(button);
        yield return null;
    }
}
}
