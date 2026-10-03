using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The client's side of an open trade beside a recording connection (Gameplay Systems §16; Prototype Content §2,
///     §4; findings of the Milestone 14 review): a bar press held as a key is, a second request refused before it is
///     sent, a request heard while a map loads stamped all the same, a field that lets the keys go when its edit ends,
///     and an amount of 0 that takes a row back.
/// </summary>
public sealed class TradeHoldTests
{
    private static readonly EntityId Local = new(100);

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

    private static NavigationGrid YardGrid()
    {
        NavigationCell[] cells = Enumerable.Repeat(NavigationCell.Level(NavigationSurface.Floor, 0f), 16).ToArray();
        return new NavigationGrid(4, 4, 1f, 0f, 0f, 0.3f, 0.4f, cells);
    }

    private static WorldEntered Entered()
    {
        return new WorldEntered(
            new MapDefinitionId("map.training_ground"),
            1,
            Local,
            new JobDefinitionId("job.adventurer"),
            0,
            new WorldPosition(1.5f, 0f, 1.5f),
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
    }

    private static byte[] Encode(TradeEvent message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        message.Write(bytes);
        return bytes;
    }

    private (GameClient Client, RecordingConnection Recording) Trading()
    {
        GameClient client = CreateIdleClient();
        var recording = RecordingConnection.EnterWorld(client, Entered(), YardGrid());
        recording.Deliver(Encode(new TradeEvent(TradeEventKind.Opened, "Bobby")));
        recording.Sent.Clear();
        return (client, recording);
    }

    [Test]
    public void ABarPress_WhileTrading_SaysTheCharacterIsHeld_AndSendsNothing()
    {
        (GameClient client, RecordingConnection recording) = Trading();

        client.UseSkillSlot(1);
        client.UseSkillSlot(4);

        Assert.That(client.ChatLog.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "You can't move while trading." }));
        Assert.That(recording.Sent, Is.Empty);
    }

    [Test]
    public void ARequest_WhileTrading_IsRefusedBeforeItIsSent()
    {
        (GameClient client, RecordingConnection recording) = Trading();

        uint sent = client.RequestTrade("Cora");

        Assert.That(sent, Is.Zero);
        Assert.That(client.ChatLog.Lines.Select(line => line.Text), Has.Member("You are already trading."));
        Assert.That(recording.SentOf(MessageOpcode.TradeRequest), Is.Empty);
    }

    // The client holds no world of its own while a map loads; the connection's world hears the request, and its 30 s
    // start then.
    [Test]
    public void ARequest_HeardWhileAMapLoads_IsStampedOnTheConnectionsWorld()
    {
        GameClient client = CreateIdleClient();
        var recording = RecordingConnection.EnterWorld(client, Entered(), YardGrid());
        typeof(GameClient).GetField("m_world", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, null);
        var requested = new TradeEvent(TradeEventKind.Requested, "Bobby");

        recording.Deliver(Encode(requested));
        typeof(GameClient)
            .GetMethod("OnTradeEvent", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(client, new object[] { requested });

        ClientTrade trade = recording.Connection.World!.Trade;
        Assert.That(trade.Requester, Is.EqualTo("Bobby"));
        Assert.That(trade.RequestEndsAt, Is.LessThan(double.MaxValue), "its 30 s started");
    }

    // Enter ends an edit and leaves the field selected, which never deselects it.
    [Test]
    public void AField_LetsTheKeysGo_WhenItsEditEnds()
    {
        GameClient client = CreateIdleClient();
        var fieldObject = new GameObject("Amount", typeof(RectTransform));
        m_created.Add(fieldObject);
        TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
        client.HoldKeysWhileFocused(field);

        field.onSelect.Invoke(string.Empty);
        bool isHeld = client.IsTyping;
        field.onEndEdit.Invoke("5");

        Assert.That((isHeld, client.IsTyping), Is.EqualTo((true, false)));
    }

    [Test]
    public void AnAmountOfNothing_TakesTheRowBack_AndAnEmptyFieldOffersOne()
    {
        GameClient client = CreateIdleClient();
        var window = TradeWindow.Create(client);
        m_created.Add(window.gameObject);

        window.AmountInput!.text = "0";
        uint none = window.Amount;
        window.AmountInput.text = string.Empty;

        Assert.That((none, window.Amount), Is.EqualTo((0u, 1u)));
    }
}
}
