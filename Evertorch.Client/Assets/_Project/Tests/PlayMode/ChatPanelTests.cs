using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The chat panel (Prototype Content §2, §4): Enter opens the input and shuts the gameplay keys, Enter sends and
///     does not open it again, Esc closes it, and the log shows what was said with rich text off. The keys come
///     through <see cref="ChatPanel.HandleKeys" />; that real keys reach no gameplay action while it is open is
///     <c>SharedIntentPathTests</c>'.
/// </summary>
public sealed class ChatPanelTests
{
    private const string ActionsAsset = "_Project/Settings/InputSystem_Actions.inputactions";

    private readonly List<Object> m_created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (InputActionAsset actions in m_created.OfType<InputActionAsset>())
        {
            actions.Disable();
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

    private (GameClient Client, RecordingConnection Recording, InputActionAsset Actions) CreateClientInTheWorld()
    {
        string actionsPath = Path.Combine(Application.dataPath, ActionsAsset);
        if (!File.Exists(actionsPath))
        {
            Assert.Ignore("The input actions asset is only readable from the editor project.");
        }

        var actions = InputActionAsset.FromJson(File.ReadAllText(actionsPath));
        m_created.Add(actions);
        var clientObject = new GameObject("TestClient");
        clientObject.SetActive(false);
        m_created.Add(clientObject);
        GameClient client = clientObject.AddComponent<GameClient>();
        typeof(GameClient)
            .GetField("m_inputActions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, actions);
        NavigationCell[] cells = Enumerable.Repeat(NavigationCell.Level(NavigationSurface.Floor, 0f), 16).ToArray();
        var grid = new NavigationGrid(4, 4, 1f, 0f, 0f, 0.3f, 0.4f, cells);
        var entered = new WorldEntered(
            new MapDefinitionId("map.training_ground"),
            1,
            new EntityId(100),
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
        var recording = RecordingConnection.EnterWorld(client, entered, grid);
        return (client, recording, actions);
    }

    private ChatPanel CreatePanel(GameClient client)
    {
        var panel = ChatPanel.Create(client);
        m_created.Add(panel.gameObject);
        return panel;
    }

    [UnityTest]
    public IEnumerator Enter_OpensTheInput_AndShutsTheGameplayKeys_AndEscClosesIt()
    {
        (GameClient client, RecordingConnection _, InputActionAsset actions) = CreateClientInTheWorld();
        ChatPanel panel = CreatePanel(client);
        InputAction move = actions.FindAction("Player/Move", true);
        move.Enable();
        yield return null;

        panel.HandleKeys(client.World, true, false);
        bool wasTyping = panel.IsTyping;
        bool wasShut = client.IsTyping && !move.enabled;
        bool wasShown = panel.IsVisible && panel.Input!.gameObject.activeSelf;
        panel.HandleKeys(client.World, false, true);

        Assert.That((wasTyping, wasShut, wasShown), Is.EqualTo((true, true, true)));
        Assert.That(panel.IsTyping, Is.False);
        Assert.That(client.IsTyping, Is.False);
        Assert.That(move.enabled, Is.True, "the walk key acts again");
    }

    [UnityTest]
    public IEnumerator Enter_SendsWhatWasTyped_SaidNearby_AndDoesNotOpenTheInputAgain()
    {
        (GameClient client, RecordingConnection recording, InputActionAsset _) = CreateClientInTheWorld();
        ChatPanel panel = CreatePanel(client);
        yield return null;

        panel.HandleKeys(client.World, true, false);
        panel.Input!.text = "hello all";
        panel.HandleKeys(client.World, true, false);
        bool isTypingAfterSending = panel.IsTyping;
        panel.HandleKeys(client.World, true, false);
        bool isReopenedByTheSameEnter = panel.IsTyping;
        yield return null;
        panel.HandleKeys(client.World, true, false);
        panel.Input.text = "   ";
        panel.HandleKeys(client.World, true, false);
        yield return null;
        panel.HandleKeys(client.World, true, false);
        panel.HandleKeys(null, false, false);
        yield return null;

        byte[][] sent = recording.SentOf(MessageOpcode.ChatSend).ToArray();
        Assert.That(isTypingAfterSending, Is.False, "the Enter that sends closes the input");
        Assert.That(isReopenedByTheSameEnter, Is.False, "and the field hearing it too does not open it again");
        Assert.That(panel.IsTyping, Is.False, "leaving the world closes it");
        Assert.That(client.IsTyping, Is.False);
        Assert.That(sent, Has.Length.EqualTo(1), "only spaces are not sent");
        Assert.That(ChatSend.TryRead(sent[0], out ChatSend? line), Is.True);
        Assert.That((line!.Channel, line.Recipient, line.Text), Is.EqualTo((ChatChannel.Nearby, "", "hello all")));
    }

    private static IEnumerator Type(ChatPanel panel, GameClient client, string typed)
    {
        panel.HandleKeys(client.World, true, false);
        panel.Input!.text = typed;
        panel.HandleKeys(client.World, true, false);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Commands_WhisperAndSpeakToTheParty_AndARefusedWhisperNamesWhoWasNotOnline()
    {
        (GameClient client, RecordingConnection recording, InputActionAsset _) = CreateClientInTheWorld();
        ChatPanel panel = CreatePanel(client);
        yield return null;

        yield return Type(panel, client, "/w Bobby psst");
        yield return Type(panel, client, "/p ready");
        yield return Type(panel, client, "/w Ann0 hi");
        uint whisper = ChatSend.TryRead(recording.SentOf(MessageOpcode.ChatSend).First(), out ChatSend? first)
            ? first!.CommandSequence
            : 0;
        uint party = recording.SentOf(MessageOpcode.ChatSend)
            .Select(payload => ChatSend.TryRead(payload, out ChatSend? line) ? line! : null!)
            .Single(line => line.Channel == ChatChannel.Party)
            .CommandSequence;
        foreach (uint refused in new[] { whisper, party })
        {
            var rejection = new CommandRejected(
                refused,
                refused == whisper ? CommandRejectionReason.InvalidTarget : CommandRejectionReason.NotAllowedNow);
            byte[] payload = new byte[CommandRejected.EncodedLength];
            rejection.Write(payload);
            recording.Deliver(payload);
        }

        yield return null;

        Assert.That(
            recording.SentOf(MessageOpcode.ChatSend)
                .Select(payload =>
                    ChatSend.TryRead(payload, out ChatSend? line) ? (line!.Channel, line.Recipient) : default),
            Is.EqualTo(new[] { (ChatChannel.Whisper, "Bobby"), (ChatChannel.Party, "") }),
            "a whisper to oneself is not sent");
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "You cannot whisper to yourself.", "Bobby is not online.", "You are not in a party." }));
    }

    [UnityTest]
    public IEnumerator TypingFasterThanTheBucket_IsRefusedByTheClient_AndNotSent()
    {
        (GameClient client, RecordingConnection recording, InputActionAsset _) = CreateClientInTheWorld();
        ChatPanel panel = CreatePanel(client);
        yield return null;

        for (int line = 1; line <= ChatThrottle.Burst + 1; line++)
        {
            yield return Type(panel, client, $"line {line}");
        }

        Assert.That(recording.SentOf(MessageOpcode.ChatSend).Count(), Is.EqualTo(ChatThrottle.Burst));
        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Is.EqualTo(new[] { "You are typing too fast." }));
    }

    [UnityTest]
    public IEnumerator LinesSaid_ShowInTheLog_WithRichTextOff_OnTheDesktopAtTheBottomLeft()
    {
        (GameClient client, RecordingConnection _, InputActionAsset _) = CreateClientInTheWorld();
        ChatPanel panel = CreatePanel(client);
        yield return null;

        client.ChatLog.Add(new ChatReceived(ChatChannel.Nearby, new EntityId(7), "Anna", "<b>hi</b>"));
        client.ChatLog.Add(new ChatReceived(ChatChannel.Whisper, default, "Bobby", "psst"));
        yield return null;

        TMP_Text[] labels = panel.GetComponentsInChildren<TMP_Text>(true)
            .Where(label => label.name.StartsWith("Line", StringComparison.Ordinal))
            .ToArray();
        var rect = (RectTransform)panel.transform.Find("Panel");
        Assert.That(panel.Text, Is.EqualTo("Anna: <b>hi</b>\nFrom Bobby: psst"));
        Assert.That(labels, Has.Length.EqualTo(ChatPanel.DesktopLines));
        Assert.That(labels.Select(label => label.richText), Has.All.False, "a line shows the letters it holds");
        Assert.That(panel.Input!.richText, Is.False);
        Assert.That(
            (rect.anchoredPosition, rect.sizeDelta),
            Is.EqualTo((ChatPanel.DesktopBounds.position, ChatPanel.DesktopBounds.size)));
    }
}
}
