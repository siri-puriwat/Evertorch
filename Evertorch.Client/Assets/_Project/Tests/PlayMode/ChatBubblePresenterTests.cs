using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A line said nearby shows over its speaker for 5 s, the local player's over its own body, with rich text off
///     (Prototype Content §2).
/// </summary>
public sealed class ChatBubblePresenterTests
{
    private static readonly EntityId Local = new(100);
    private static readonly EntityId Other = new(200);

    private readonly List<Object> m_created = new();
    private ChatBubblePresenter? m_presenter;

    [TearDown]
    public void TearDown()
    {
        m_presenter?.Dispose();
        m_presenter = null;
        foreach (TextMeshPro text in Object.FindObjectsByType<TextMeshPro>(FindObjectsSortMode.None))
        {
            if (text.name == "ChatBubble")
            {
                Object.DestroyImmediate(text.gameObject);
            }
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

    private EntityView CreateView(string objectName, Vector3 position)
    {
        var root = new GameObject(objectName);
        root.transform.position = position;
        m_created.Add(root);
        return root.AddComponent<EntityView>();
    }

    private static TextMeshPro[] Bubbles()
    {
        var bubbles = new List<TextMeshPro>();
        foreach (TextMeshPro text in Object.FindObjectsByType<TextMeshPro>(FindObjectsSortMode.None))
        {
            if (text.name == "ChatBubble")
            {
                bubbles.Add(text);
            }
        }

        return bubbles.ToArray();
    }

    [Test]
    public void Say_ShowsTheLineOverItsSpeaker_TheLocalPlayersOverItsOwnBody()
    {
        m_presenter = new ChatBubblePresenter(Local);
        EntityView local = CreateView("Local", new Vector3(1f, 0f, 1f));
        var remotes = new Dictionary<EntityId, EntityView> { { Other, CreateView("Other", new Vector3(4f, 0f, 1f)) } };

        m_presenter.Say(Other, "<b>hello</b>", 10f);
        m_presenter.Say(Local, "hi back", 10f);
        m_presenter.Present(local, remotes, null, 11f);

        Assert.That(m_presenter.TryGetText(Other, out string other), Is.True);
        Assert.That(other, Is.EqualTo("<b>hello</b>"));
        Assert.That(m_presenter.TryGetText(Local, out string own), Is.True);
        Assert.That(own, Is.EqualTo("hi back"));
        TextMeshPro[] bubbles = Bubbles();
        Assert.That(bubbles, Has.Length.EqualTo(2));
        Assert.That(bubbles, Has.All.Matches<TextMeshPro>(text => !text.richText), "the letters as they were said");
        Assert.That(bubbles, Has.Some.Matches<TextMeshPro>(text =>
            Mathf.Approximately(text.transform.position.x, 4f) && text.transform.position.y > 1f));
    }

    [UnityTest]
    public IEnumerator Bubble_GoesAfterFiveSeconds_AndANewerLineReplacesIt()
    {
        m_presenter = new ChatBubblePresenter(Local);
        var remotes = new Dictionary<EntityId, EntityView> { { Other, CreateView("Other", Vector3.zero) } };

        m_presenter.Say(Other, "first", 10f);
        m_presenter.Say(Other, "second", 12f);
        m_presenter.Present(null, remotes, null, 16.9f);
        bool isShownBeforeItsEnd = m_presenter.TryGetText(Other, out string shown);
        m_presenter.Present(null, remotes, null, 17f);
        yield return null;

        Assert.That((isShownBeforeItsEnd, shown), Is.EqualTo((true, "second")), "five seconds from the newer line");
        Assert.That(m_presenter.TryGetText(Other, out _), Is.False);
        Assert.That(Bubbles(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator Bubble_OfASpeakerNoLongerInView_Goes()
    {
        m_presenter = new ChatBubblePresenter(Local);
        var remotes = new Dictionary<EntityId, EntityView> { { Other, CreateView("Other", Vector3.zero) } };
        m_presenter.Say(Other, "bye", 10f);

        remotes.Clear();
        m_presenter.Present(null, remotes, null, 11f);
        yield return null;

        Assert.That(m_presenter.TryGetText(Other, out _), Is.False);
        Assert.That(Bubbles(), Is.Empty);
    }
}
}
