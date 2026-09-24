using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The panels a player sees (Prototype Content §2), built for a client that never starts, so they show only what
///     they are given. The live tests use them against a server.
/// </summary>
public sealed class PlayerPanelTests
{
    private readonly List<Object> m_created = new();

    [TearDown]
    public void DestroyCreated()
    {
        foreach (Object created in m_created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        m_created.Clear();
    }

    // Never activated, so the client neither loads content nor connects.
    private GameClient CreateIdleClient()
    {
        var clientObject = new GameObject("TestClient");
        clientObject.SetActive(false);
        m_created.Add(clientObject);
        return clientObject.AddComponent<GameClient>();
    }

    private static TMP_Text Label(Component panel, string objectName)
    {
        return panel.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == objectName);
    }

    [UnityTest]
    public IEnumerator LoginPanel_WhileDisconnected_ShowsTheConnectFormOnly_AndKeepsTheIdentityInTheClient()
    {
        GameClient client = CreateIdleClient();
        var login = LoginPanel.Create(client);
        m_created.Add(login.gameObject);
        yield return null;

        GameObject[] shown = login.GetComponentsInChildren<Transform>()
            .Select(child => child.gameObject)
            .Where(child => child.name == "Connect" || child.name == "Characters" || child.name == "Reconnect")
            .ToArray();
        TMP_InputField identity = login.GetComponentsInChildren<TMP_InputField>(true)
            .Single(field => field.transform.parent.name == "Identity");
        identity.text = "someone";

        Assert.That(login.IsVisible, Is.True);
        Assert.That(shown.Select(child => child.name), Is.EquivalentTo(new[] { "Connect", "Connect" }),
            "the connect form and its Connect button; no characters and no Reconnect before any close");
        Assert.That(client.Identity, Is.EqualTo("someone"), "the identity lives in the client, in memory");
    }

    [UnityTest]
    public IEnumerator StatusBar_RewritesAPartOnlyWhenItsValueChanges()
    {
        var bar = StatusBar.Create(CreateIdleClient());
        m_created.Add(bar.gameObject);
        yield return null;

        bar.ShowCharacter("Tester", 3);
        bar.ShowHealth(50, 71, false);
        bar.ShowMap("Training Ground");
        bar.ShowPing(42);
        int afterFirst = bar.TextChanges;
        bar.ShowCharacter("Tester", 3);
        bar.ShowHealth(50, 71, false);
        bar.ShowMap("Training Ground");
        bar.ShowPing(42);
        int afterRepeat = bar.TextChanges;
        bar.ShowHealth(0, 71, true);

        Assert.That(afterFirst, Is.EqualTo(4));
        Assert.That(afterRepeat, Is.EqualTo(4), "the same values rewrite nothing");
        Assert.That(bar.TextChanges, Is.EqualTo(5));
        Assert.That(Label(bar, "Name").text, Is.EqualTo("Tester   Lv 3"));
        Assert.That(Label(bar, "Health").text, Is.EqualTo("HP 0 / 71   You died"));
        Assert.That(Label(bar, "Map").text, Is.EqualTo("Training Ground"));
        Assert.That(Label(bar, "Ping").text, Is.EqualTo("42 ms"));
        Assert.That(bar.IsVisible, Is.False, "out of the world the bar stays hidden");
    }
}
}
