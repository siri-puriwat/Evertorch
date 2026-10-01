using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The Milestone 12 "Playing together" presentation checks (ROADMAP §8; Coding Standards §10): the real
///     <see cref="GameClient" /> against the real server process and a real database. The headless
///     <c>PlayingTogetherAcceptanceTests</c> hold the server's steps; here each later line adds a bounded check of
///     what the real client draws: the name plate, chat, the party list, and a member's bar.
/// </summary>
public sealed class LiveServerSocialTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const string AnnName = "LiveSocialAnn";
    private const string BobName = "LiveSocialBob";
    private const float StartTimeoutSeconds = 30f;
    private const int TestTimeoutMs = 300_000;

    private LiveDatabase? m_database;
    private LiveServer? m_server;
    private GameObject? m_client;
    private InputActionAsset? m_actions;
    private LiteNetLibClientTransport? m_otherSocket;
    private ClientConnection? m_other;

    [UnityTearDown]
    public IEnumerator StopEverything()
    {
        if (m_client != null)
        {
            Object.Destroy(m_client);
            m_client = null;
        }

        m_other = null;
        m_otherSocket?.Dispose();
        m_otherSocket = null;
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

    // Chat through the real panel against the real server (Prototype Content §2, §4): the other player's line said
    // nearby reaches the log and shows over it; a whisper typed as "/w Name text" reaches only it and comes back as
    // sent; a whisper to no one comes back as "Nobody1 is not online.".
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Chat_ThroughTheRealPanel_IsHeard_Whispered_AndRefusedInWords()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, server.JoinOutput());
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, AnnName);
        ClientWorld world = client.World!;
        yield return EnterTheOther(client, port);
        ClientConnection other = m_other!;
        EntityId seen = other.World!.LocalEntity;
        var heard = new List<string>();
        other.ChatLineReceived += line => heard.Add(ChatLog.Describe(line));
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Remotes.ContainsKey(seen);
            },
            StartTimeoutSeconds);

        other.SendChat(ChatChannel.Nearby, string.Empty, "hello from Bob");
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.ChatLog.Lines.Any(line => line.Text == $"{BobName}: hello from Bob");
            },
            StartTimeoutSeconds);
        yield return null;
        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Has.Member($"{BobName}: hello from Bob"));
        Assert.That(client.Bubbles!.TryGetText(seen, out string bubble), Is.True, "a bubble over the speaker");
        Assert.That(bubble, Is.EqualTo("hello from Bob"));

        ChatPanel panel = client.GetComponentsInChildren<ChatPanel>(true).Single();
        panel.HandleKeys(world, true, false);
        bool wasGated = client.IsTyping;
        panel.Input!.text = $"/w {BobName} psst";
        panel.HandleKeys(world, true, false);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return heard.Contains($"From {AnnName}: psst")
                    && client.ChatLog.Lines.Any(line => line.Text == $"To {BobName}: psst");
            },
            StartTimeoutSeconds);
        Assert.That(wasGated, Is.True, "typing shut the gameplay keys");
        Assert.That(heard, Has.Member($"From {AnnName}: psst"), server.JoinOutput());
        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Has.Member($"To {BobName}: psst"));

        yield return null;
        panel.HandleKeys(world, true, false);
        panel.Input.text = "/w Nobody1 anyone?";
        panel.HandleKeys(world, true, false);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return client.ChatLog.Lines.Any(line => line.Text == "Nobody1 is not online.");
            },
            StartTimeoutSeconds);
        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Has.Member("Nobody1 is not online."));
        Assert.That(client.IsTyping, Is.False);
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
    }

    // The other player: the client's own networking without Unity's views, as a second window would run it.
    private IEnumerator EnterTheOther(GameClient client, int port)
    {
        m_otherSocket = new LiteNetLibClientTransport("evertorch", 5000);
        ClientConnection other = m_other = new ClientConnection(
            m_otherSocket,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, client.Content!.Version, "dev:live-social"),
            client.Content!);
        bool hasList = false;
        other.CharactersChanged += () => hasList = true;
        other.Connect("127.0.0.1", port);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return hasList && other.State == ClientConnectionState.SelectingCharacter;
            },
            StartTimeoutSeconds);
        other.CreateCharacter(BobName);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.Characters.Any(entry => entry.Name == BobName);
            },
            StartTimeoutSeconds);
        other.EnterWorld(other.Characters.Single(entry => entry.Name == BobName).Character);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.World?.Inventory.IsCurrent == true;
            },
            StartTimeoutSeconds);
        Assert.That(other.World, Is.Not.Null, other.LocalError);
    }

    // Another player entering the training ground is seen by the real client, its name under its feet as the client's
    // own is under its own; selected, the target frame names it and its job (Prototype Content §2).
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator OtherPlayer_Entering_IsDrawnWithItsName_AndNamedInTheTargetFrame()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        LiveServer server = m_server!;
        Assert.That(server.TryReadListeningPort(out int port), Is.True, server.JoinOutput());
        GameClient client = CreateClient(actionsPath);
        yield return EnterByName(client, AnnName);
        ClientWorld world = client.World!;

        m_otherSocket = new LiteNetLibClientTransport("evertorch", 5000);
        var other = new ClientConnection(
            m_otherSocket,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, client.Content!.Version, "dev:live-social"),
            client.Content!);
        bool hasList = false;
        other.CharactersChanged += () => hasList = true;
        other.Connect("127.0.0.1", port);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return hasList && other.State == ClientConnectionState.SelectingCharacter;
            },
            StartTimeoutSeconds);
        other.CreateCharacter(BobName);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.Characters.Any(entry => entry.Name == BobName);
            },
            StartTimeoutSeconds);
        other.EnterWorld(other.Characters.Single(entry => entry.Name == BobName).Character);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return other.World?.Inventory.IsCurrent == true;
            },
            StartTimeoutSeconds);
        Assert.That(other.World, Is.Not.Null, $"{other.LocalError} {server.JoinOutput()}");
        EntityId seen = other.World!.LocalEntity;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Remotes.ContainsKey(seen);
            },
            StartTimeoutSeconds);

        Assert.That(world.Remotes.ContainsKey(seen), Is.True, $"{client.Status} {server.JoinOutput()}");
        NamePlatePresenter plates = client.NamePlates!;
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return plates.TryGetPlate(seen, out NamePlate? plate) && plate!.Text == BobName;
            },
            StartTimeoutSeconds);
        Assert.That(plates.TryGetPlate(seen, out NamePlate? shown) ? shown!.Text : null, Is.EqualTo(BobName));
        Assert.That(plates.LocalPlate?.Text, Is.EqualTo(AnnName), "its own name under its own feet");

        TargetFrame frame = client.GetComponentsInChildren<TargetFrame>(true).Single();
        client.Connection!.SendTarget(seen);
        yield return WaitUntil(
            () =>
            {
                other.Poll();
                return world.Target == seen && frame.IsVisible;
            },
            StartTimeoutSeconds);
        yield return null;
        Assert.That(
            frame.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Name").text,
            Is.EqualTo($"{BobName} \u00B7 Adventurer"),
            "the frame names the player and its job");
        Assert.That(other.MalformedMessages + other.UnexpectedMessages, Is.Zero, "the other player's traffic");
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
        server.Start(m_database, "--World:RandomSeed=12");
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
