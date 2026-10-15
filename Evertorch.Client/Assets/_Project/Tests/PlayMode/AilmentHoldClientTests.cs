using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The client's side of the local character's ailments beside a recording connection (Prototype Content §2;
///     Gameplay Systems §9.1): a held or silenced press says why and sends nothing, once until the ailments change, and
///     blind darkens the view.
/// </summary>
public sealed class AilmentHoldClientTests
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

    private static void Flag(ClientWorld world, uint tick, EntityStateFlags flags)
    {
        world.OnSnapshot(
            new EntitySnapshot(
                tick,
                0,
                new[]
                {
                    new EntityState(Local, world.Predictor.Position, new WorldDirection(0f, 1f), 0f, 0f, 0f, flags)
                }));
    }

    private (GameClient Client, RecordingConnection Recording) Entering()
    {
        GameClient client = CreateIdleClient();
        var recording = RecordingConnection.EnterWorld(client, Entered(), YardGrid());
        recording.Sent.Clear();
        return (client, recording);
    }

    [Test]
    public void BarPresses_WhileFrozen_SayWhy_OnceEach_AndSendNothing()
    {
        (GameClient client, RecordingConnection recording) = Entering();
        Flag(client.World!, 1, EntityStateFlags.Frozen);

        client.UseSkillSlot(1);
        client.UseSkillSlot(2);
        client.UseSkillSlot(4);

        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "You can't use skills while frozen.", "You can't use items while frozen." }));
        Assert.That(recording.Sent, Is.Empty);
    }

    [Test]
    public void ASilence_RefusesSkillsAlone_AndANewAilmentSaysItAgain()
    {
        (GameClient client, RecordingConnection recording) = Entering();
        ClientWorld world = client.World!;
        Flag(world, 1, EntityStateFlags.Silenced);

        client.UseSkillSlot(1);
        client.UseSkillSlot(1);
        bool isHeld = world.IsHeld;
        Flag(world, 2, EntityStateFlags.Silenced | EntityStateFlags.Asleep);
        client.UseSkillSlot(1);

        Assert.That(isHeld, Is.False, "a silence holds nothing");
        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "You can't use skills while silenced.", "You can't use skills while asleep." }));
        Assert.That(recording.Sent, Is.Empty);
    }

    // A freeze that ends and comes back says its words again, with no press between (review of Milestone 19).
    [Test]
    public void AHoldThatEndsAndComesBack_SaysItsWordsAgain()
    {
        (GameClient client, RecordingConnection recording) = Entering();
        ClientWorld world = client.World!;
        Flag(world, 1, EntityStateFlags.Frozen);
        client.UseSkillSlot(1);
        Flag(world, 2, EntityStateFlags.None);
        Flag(world, 3, EntityStateFlags.Frozen);
        client.UseSkillSlot(1);

        Assert.That(
            client.ChatLog.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "You can't use skills while frozen.", "You can't use skills while frozen." }));
        Assert.That(recording.Sent, Is.Empty);
    }

    // A held player begins no trade and answers none, as the server would refuse both; the request waits.
    [Test]
    public void ATrade_WhileHeld_IsNeitherAskedNorAnswered()
    {
        (GameClient client, RecordingConnection recording) = Entering();
        ClientWorld world = client.World!;
        Flag(world, 1, EntityStateFlags.Asleep);
        world.Trade.Apply(new TradeEvent(TradeEventKind.Requested, "Bobby"));

        uint asked = client.RequestTrade("Bobby");
        uint answered = client.AnswerTradeRequest(true);

        Assert.That((asked, answered), Is.EqualTo((0u, 0u)));
        Assert.That(client.ChatLog.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "You can't trade while asleep." }));
        Assert.That(recording.SentOf(MessageOpcode.TradeRequest).Concat(recording.SentOf(MessageOpcode.TradeReply)),
            Is.Empty);
        Assert.That(world.Trade.Requester, Is.EqualTo("Bobby"), "the request still waits");
    }

    [UnityTest]
    public IEnumerator Blind_DarkensTheView_TillItEnds()
    {
        (GameClient client, _) = Entering();
        var vignette = BlindVignette.Create(client);
        m_created.Add(vignette.gameObject);
        yield return null;
        bool isShownFirst = vignette.IsShown;

        Flag(client.World!, 1, EntityStateFlags.Blinded | EntityStateFlags.Poisoned);
        yield return null;
        bool isShownBlinded = vignette.IsShown;
        Flag(client.World!, 2, EntityStateFlags.Poisoned);
        yield return null;

        Assert.That((isShownFirst, isShownBlinded), Is.EqualTo((false, true)));
        Assert.That(vignette.IsShown, Is.False);
        Assert.That(vignette.GetComponent<Canvas>().sortingOrder, Is.LessThan(5),
            "under the combat HUD and the windows");
    }
}
}
