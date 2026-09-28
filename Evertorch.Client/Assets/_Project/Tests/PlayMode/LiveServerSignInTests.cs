using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The real client's sign-in at the real server's gateway (Network Protocol §4): what the login panel says when it
///     is refused, and that the pinned certificate, not the machine's trust, decides which gateway it believes.
/// </summary>
public sealed class LiveServerSignInTests : InputTestFixture
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";
    private const float StartTimeoutSeconds = 30f;
    private const int TestTimeoutMs = 180_000;

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

        // As in LiveServerCombatTests: only a scene the client loaded is unloaded, never the test runner's own.
        Scene loaded = SceneManager.GetActiveScene();
        Scene empty = SceneManager.CreateScene($"Empty {Guid.NewGuid():N}");
        SceneManager.SetActiveScene(empty);
        if (loaded.isLoaded
            && (MapSceneResolver.IsMapScene(loaded.name) || loaded.name == BootstrapRedirect.MainMenuScene))
        {
            yield return SceneManager.UnloadSceneAsync(loaded);
        }
    }

    // A thumbprint the gateway's certificate does not have: the pin must refuse it although Windows trusts the
    // development certificate, then a wrong password is refused in the client's words, then the right one signs in.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator SignIn_WithTheWrongPinThenTheWrongPassword_IsRefusedInTheClientsWords_ThenSignsIn()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        GameClient client = CreateClient(actionsPath);
        string thumbprint = client.PinnedThumbprint!;
        client.PinnedThumbprint = new string('0', thumbprint.Length);

        yield return LiveSignIn.PressConnect(client);
        yield return WaitUntil(() => !client.IsSigningIn, StartTimeoutSeconds);
        string wrongPin = client.Status;
        Assert.That(client.Connection, Is.Null, "no game connection without a sign-in");

        client.PinnedThumbprint = thumbprint;
        SetPassword(client, "Wrong-Password-1");
        PressConnect(client);
        yield return WaitUntil(() => !client.IsSigningIn, StartTimeoutSeconds);
        string wrongPassword = client.Status;

        SetPassword(client, LiveServer.AccountPassword);
        PressConnect(client);
        yield return WaitUntil(
            () => client.Connection?.State == ClientConnectionState.SelectingCharacter,
            StartTimeoutSeconds);

        Assert.That(wrongPin, Is.EqualTo(SignInMessages.ForStatus(0)), "the pin refused the gateway");
        Assert.That(wrongPassword, Is.EqualTo("Wrong login or password."));
        Assert.That(client.Connection?.State, Is.EqualTo(ClientConnectionState.SelectingCharacter), client.Status);
        Assert.That(m_server!.JoinOutput(),
            Does.Not.Contain(LiveServer.AccountPassword).And.Not.Contain("Wrong-Password-1"));
    }

    // Network Protocol §7: the browser's path in the editor, through the managed WebSocket with the pin, which is also
    // where Unity's Mono proves it can speak wss:// at all.
    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator SignIn_OverWebSocket_EntersTheWorldThroughTheGateway()
    {
        string actionsPath = RequirePrerequisites();
        yield return StartDatabaseAndServer();
        GameClient client = CreateClient(actionsPath);
        client.UsesWebSocket = true;

        yield return LiveSignIn.PressConnect(client);
        yield return WaitUntil(
            () => client.Connection?.State == ClientConnectionState.SelectingCharacter,
            StartTimeoutSeconds);
        Assert.That(client.Connection?.State, Is.EqualTo(ClientConnectionState.SelectingCharacter), client.Status);
        client.CreateCharacter("WebLive1");
        yield return WaitUntil(() => client.Connection!.Characters.Count == 1, StartTimeoutSeconds);
        client.EnterWorld(client.Connection!.Characters[0].Character);
        yield return WaitUntil(() => client.World?.Inventory.IsCurrent == true, StartTimeoutSeconds);

        Assert.That(client.World?.Inventory.IsCurrent, Is.True, client.Status);
        Assert.That(client.Link!.RoundTripMilliseconds, Is.GreaterThanOrEqualTo(0));
    }

    private static void SetPassword(GameClient client, string password)
    {
        client.GetComponentsInChildren<TMP_InputField>(true)
            .Single(field => field.transform.parent.name == "Password")
            .text = password;
    }

    private static void PressConnect(GameClient client)
    {
        Button connect = client.GetComponentsInChildren<Button>(true).Single(button => button.name == "Connect");
        Assert.That(connect.gameObject.activeInHierarchy, Is.True, "the login panel offers Connect again");
        connect.onClick.Invoke();
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
        server.Start(m_database);
        Assert.That(server.IsTiedToEditor || !KillOnCloseJob.IsSupported, Is.True, "the server ends with the editor");
        yield return WaitUntil(() => server.TryReadGateway(out int _, out string _), StartTimeoutSeconds);
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
