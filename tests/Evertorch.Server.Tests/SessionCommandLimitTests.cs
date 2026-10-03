using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Layer 2 of the abuse controls (Network Protocol §11): per-connection buckets for each class of command, counted
///     in ticks, and the cap on admission work waiting for the database (Persistence §9).
/// </summary>
[TestFixture]
public sealed class SessionCommandLimitTests
{
    private static readonly AbuseOptions Defaults = new();

    private static CommandRejected[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    private static uint SendAttacks(TestServer server, ConnectionId player, uint firstSequence, int count)
    {
        uint sequence = firstSequence;
        for (int index = 0; index < count; index++)
        {
            server.SendAttack(player, default, sequence++);
        }

        return sequence;
    }

    [Test]
    public void AdmissionWork_BeyondItsShareOfTheQueue_IsRefusedAsNotReady()
    {
        var server = new TestServer(
            persistence: new PersistenceOptions { QueueCapacity = 8, MaxAdmissionJobs = 2, RetryBaseDelayMs = 1 });
        server.RunsPersistence = false;
        ConnectionId[] hellos = { server.Connect(), server.Connect(), server.Connect() };
        server.Tick();

        foreach (ConnectionId connection in hellos)
        {
            server.SendHello(connection);
        }

        server.Tick();

        Assert.That(server.Transport.Disconnects.Keys, Is.EquivalentTo(new[] { hellos[2] }));
        Assert.That(server.Transport.Disconnects[hellos[2]], Is.EqualTo(DisconnectReason.ServerNotReady));
        Assert.That(server.Persistence.AdmissionRefusals, Is.EqualTo(1));

        server.RunsPersistence = true;
        server.Tick();
        ConnectionId later = server.Connect();
        server.SendHello(later);
        server.Tick(2);
        Assert.That(server.SessionOf(later).Account, Is.Not.Null, "the share frees up as the writer works");
    }

    [Test]
    public void Attacks_OverTheCombatBucket_AreRefusedAsNotAllowedNow()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        SendAttacks(server, player, 1, Defaults.CombatCommandBurst + 1);
        server.Tick();

        CommandRejected[] rejections = Rejections(server, player);
        Assert.That(
            rejections.Count(rejection => rejection.Reason == CommandRejectionReason.InvalidTarget),
            Is.EqualTo(Defaults.CombatCommandBurst));
        CommandRejected throttled =
            rejections.Single(rejection => rejection.Reason == CommandRejectionReason.NotAllowedNow);
        Assert.That(throttled.CommandSequence, Is.EqualTo((uint)Defaults.CombatCommandBurst + 1));
        Assert.That(server.SessionOf(player).ThrottledCommands, Is.EqualTo(1));
    }

    [Test]
    public void Cancels_AreNeverThrottled()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        for (uint sequence = 1; sequence <= 200; sequence++)
        {
            server.SendCancel(player, sequence);
        }

        server.Tick();

        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That(server.SessionOf(player).ThrottledCommands, Is.Zero);
    }

    [Test]
    public void CharacterCreations_OverTheSessionBucket_AreDroppedWithoutAnAnswer()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SignIn(connection);

        for (int attempt = 0; attempt < Defaults.SessionCommandBurst + 1; attempt++)
        {
            server.SendCreateCharacter(connection, $"Limited{attempt}");
            server.Tick();
        }

        int answers = server.Transport.ControlOpcodesSentTo(connection)
            .Count(opcode => opcode == MessageOpcode.CreateCharacterResult);
        Assert.That(answers, Is.EqualTo(Defaults.SessionCommandBurst));
        Assert.That(server.SessionOf(connection).ThrottledCommands, Is.EqualTo(1));
    }

    [Test]
    public void CombatBucket_RefillsWithTheTicksThatPass()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        uint next = SendAttacks(server, player, 1, Defaults.CombatCommandBurst);
        server.Tick(TestServer.TickRate);
        server.Transport.ClearSent();

        SendAttacks(server, player, next, Defaults.CombatCommandsPerSecond + 1);
        server.Tick();

        CommandRejected[] rejections = Rejections(server, player);
        Assert.That(
            rejections.Count(rejection => rejection.Reason == CommandRejectionReason.NotAllowedNow),
            Is.EqualTo(1),
            "one second of ticks gave back one second of commands");
    }

    // Each kind of inbound event and the class whose bucket it takes a token from, empty for none, so a new command
    // cannot fall through to no bucket unnoticed (Network Protocol §11).
    [Test]
    public void EveryInboundKind_TakesFromTheClassPinnedHere()
    {
        var classes = new Dictionary<InboundEventKind, string>
        {
            [InboundEventKind.Connected] = string.Empty,
            [InboundEventKind.Disconnected] = string.Empty,
            [InboundEventKind.Hello] = string.Empty,
            [InboundEventKind.EnterWorld] = ServerInstruments.SessionCommandLimit,
            [InboundEventKind.Malformed] = string.Empty,
            [InboundEventKind.Move] = string.Empty,
            [InboundEventKind.Target] = ServerInstruments.CombatCommandLimit,
            [InboundEventKind.Attack] = ServerInstruments.CombatCommandLimit,
            [InboundEventKind.Cancel] = string.Empty,
            [InboundEventKind.Respawn] = ServerInstruments.CombatCommandLimit,
            [InboundEventKind.CreateCharacter] = ServerInstruments.SessionCommandLimit,
            [InboundEventKind.Logout] = ServerInstruments.SessionCommandLimit,
            [InboundEventKind.InventoryResync] = ServerInstruments.ResyncRequestLimit,
            [InboundEventKind.Pickup] = ServerInstruments.PickupCommandLimit,
            [InboundEventKind.RateLimited] = string.Empty,
            [InboundEventKind.InputDropped] = string.Empty,
            [InboundEventKind.UseSkill] = ServerInstruments.CombatCommandLimit,
            [InboundEventKind.Equip] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.Unequip] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.UseItem] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.Buy] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.Sell] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.AcceptQuest] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.CompleteQuest] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.AllocateStat] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.LearnSkill] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.ResetBuild] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.ChangeJob] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.Chat] = ServerInstruments.ChatCommandLimit,
            [InboundEventKind.PartyInvite] = ServerInstruments.PartyCommandLimit,
            [InboundEventKind.PartyReply] = ServerInstruments.PartyCommandLimit,
            [InboundEventKind.PartyLeave] = ServerInstruments.PartyCommandLimit,
            [InboundEventKind.PartyKick] = ServerInstruments.PartyCommandLimit,
            [InboundEventKind.PartyLead] = ServerInstruments.PartyCommandLimit,
            [InboundEventKind.TradeRequest] = ServerInstruments.TradeCommandLimit,
            [InboundEventKind.TradeReply] = ServerInstruments.TradeCommandLimit,
            [InboundEventKind.TradeOffer] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.TradeLock] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.TradeConfirm] = ServerInstruments.ItemCommandLimit,
            [InboundEventKind.TradeCancel] = ServerInstruments.ItemCommandLimit
        };
        var limits = new SessionCommandLimits(Defaults, TestServer.TickRate, 0);

        Assert.That(classes.Keys, Is.EquivalentTo(Enum.GetValues(typeof(InboundEventKind))));
        foreach (KeyValuePair<InboundEventKind, string> expected in classes)
        {
            limits.TryTake(expected.Key, 0, out string limit);
            Assert.That(limit, Is.EqualTo(expected.Value), expected.Key.ToString());
        }
    }

    [Test]
    public void ItemCommands_OverTheItemBucket_AreRefusedAsNotAllowedNow_EveryKindFromOneBucket()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        uint sequence = 1;
        for (int index = 0; index < Defaults.ItemCommandBurst / 2; index++)
        {
            server.SendEquip(player, 999999, sequence++);
            server.SendUnequip(player, EquipmentSlot.Weapon, sequence++);
        }

        server.SendUseItem(player, 999999, sequence);
        server.Tick();

        CommandRejected[] rejections = Rejections(server, player);
        Assert.That(
            rejections.Take(Defaults.ItemCommandBurst).Select(rejection => rejection.Reason),
            Is.All.EqualTo(CommandRejectionReason.InvalidTarget),
            "no such row, and nothing worn");
        Assert.That(rejections.Last().Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(server.SessionOf(player).ThrottledCommands, Is.EqualTo(1));
    }

    [Test]
    public void LimitsOff_NothingIsThrottled()
    {
        var server = new TestServer(isAbuseControlEnabled: false);
        ConnectionId player = server.EnterWorld(7);

        SendAttacks(server, player, 1, 500);
        server.Tick();

        Assert.That(Rejections(server, player)
            .All(rejection => rejection.Reason == CommandRejectionReason.InvalidTarget));
        Assert.That(server.SessionOf(player).ThrottledCommands, Is.Zero);
    }

    [Test]
    public void Pickups_OverThePickupBucket_AreRefusedAsNotAllowedNow()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        for (uint sequence = 1; sequence <= Defaults.PickupCommandBurst + 1; sequence++)
        {
            server.SendPickup(player, new EntityId(999999), sequence);
        }

        server.Tick();

        Assert.That(
            Rejections(server, player).Count(rejection => rejection.Reason == CommandRejectionReason.NotAllowedNow),
            Is.EqualTo(1));
    }

    [Test]
    public void Reconnect_StartsWithFullBuckets()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(7);
        SendAttacks(server, first, 1, Defaults.CombatCommandBurst + 1);
        server.Tick();
        server.Disconnect(first);
        server.Tick();

        ConnectionId second = server.EnterWorld(7);
        SendAttacks(server, second, 1000, Defaults.CombatCommandBurst);
        server.Tick();

        Assert.That(server.SessionOf(second).ThrottledCommands, Is.Zero);
    }

    [Test]
    public void ResyncRequests_OverTheResyncBucket_AreDroppedAndCounted()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        for (int request = 0; request < Defaults.ResyncRequestBurst + 1; request++)
        {
            server.SendInventoryResync(player);
        }

        server.Tick();

        Assert.That(server.SessionOf(player).ThrottledCommands, Is.EqualTo(1));
    }

    [Test]
    public void Skills_TakeFromTheCombatBucketWithAttacks_AndOverItAreRefusedAsNotAllowedNow()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        uint focus = SendAttacks(server, player, 1, Defaults.CombatCommandBurst);
        server.SendUseSkill(player, "skill.focus", default, focus);
        server.Tick();

        CommandRejected throttled = Rejections(server, player)
            .Single(rejection => rejection.Reason == CommandRejectionReason.NotAllowedNow);
        Assert.That(throttled.CommandSequence, Is.EqualTo(focus));
        Assert.That(server.SessionOf(player).ThrottledCommands, Is.EqualTo(1));
        Assert.That(server.PlayerOf(player).StatusEffects, Is.Empty, "the refused Focus was never cast");
    }

    [Test]
    public void Targets_OverTheCombatBucket_AreDroppedAndCounted()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);

        for (int request = 0; request < Defaults.CombatCommandBurst + 5; request++)
        {
            server.SendTarget(player, default);
        }

        server.Tick();

        Assert.That(server.SessionOf(player).ThrottledCommands, Is.EqualTo(5));
        Assert.That(server.SessionManager.ThrottledCommands, Is.EqualTo(5));
    }

    [Test]
    public void TownCommands_OverTheItemBucket_AreRefusedAsNotAllowedNow_FromTheSameBucket()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        var nobody = new EntityId(999999);

        uint sequence = 1;
        for (int index = 0; index < Defaults.ItemCommandBurst / 4; index++)
        {
            server.SendBuy(player, nobody, "item.consumable.minor_health", 1, sequence++);
            server.SendSell(player, nobody, 999999, 1, sequence++);
            server.SendAcceptQuest(player, nobody, "quest.crawler_hunt", sequence++);
            server.SendCompleteQuest(player, nobody, "quest.crawler_hunt", sequence++);
        }

        server.SendCompleteQuest(player, nobody, "quest.crawler_hunt", sequence);
        server.Tick();

        CommandRejected[] rejections = Rejections(server, player);
        Assert.That(
            rejections.Take(Defaults.ItemCommandBurst).Select(rejection => rejection.Reason),
            Is.All.EqualTo(CommandRejectionReason.InvalidTarget),
            "no such NPC");
        Assert.That(rejections.Last().Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(server.SessionOf(player).ThrottledCommands, Is.EqualTo(1));
    }
}
}
