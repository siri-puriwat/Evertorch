using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Gate Warden's quest (Gameplay Systems §2.2, §6.1; Persistence §5, §6; the vectors of the quests research note
///     §4): acceptance and its refusals, the kills that count for every eligible sharer, and the turn-in, whose reward
///     reaches memory only after its commit, once.
/// </summary>
[TestFixture]
public sealed class QuestTests
{
    private const string Hunt = "quest.crawler_hunt";
    private const string GateWarden = "npc.gate_warden";
    private const string Quartermaster = "npc.quartermaster";
    private const string Field = "map.training_field";
    private const string Crawler = "monster.forest_crawler";
    private const string Wisp = "monster.spark_wisp";
    private const string Potion = "item.consumable.minor_health";
    private const long Cap = 1_000_000_000;

    private sealed class Hunter
    {
        public Hunter(TestServer server, ConnectionId player, EntityId npc)
        {
            Server = server;
            Player = player;
            Npc = npc;
        }

        public TestServer Server { get; }

        public ConnectionId Player { get; }

        public EntityId Npc { get; }

        public CharacterSession Character => Server.SessionOf(Player).Character!;

        // Answers the command sent last, once its tick and, if it passed, its commit have run.
        public CommandRejectionReason Answer()
        {
            Server.Tick();
            Server.TickUntil(() => Character.Operation == null);
            return Server.Transport.ControlSentTo(Player)
                .Where(message => message.Opcode == MessageOpcode.CommandRejected)
                .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
                .DefaultIfEmpty(CommandRejectionReason.None)
                .Last();
        }

        public List<string> Logs()
        {
            return LogsSentTo(Server, Player);
        }
    }

    // Character 1, with the quest stored as given and what prepare gives it, enters the training ground and stands the
    // given distance east of the NPC.
    private static Hunter AtThe(
        string npc,
        float distance = 2f,
        int? progress = null,
        bool isCompleted = false,
        long coins = 0,
        int level = 1,
        long experience = 0,
        Action<InMemoryGameStore>? prepare = null)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        prepare?.Invoke(server.Store);
        if (progress != null)
        {
            server.Store.GiveQuest(1, Hunt, progress.Value, isCompleted);
        }

        server.Store.Edit(1, coins: coins, level: level, experience: experience);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity entity = server.NpcOf(npc);
        server.Place(player, entity.Position.X + distance, entity.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();
        return new Hunter(server, player, entity.Id);
    }

    private static TestServer FieldServer(int reconnectGraceMs = 0)
    {
        return new TestServer(
            withMonsters: true,
            withMonsterAi: false,
            withEveryMap: true,
            reconnectGraceMs: reconnectGraceMs);
    }

    // Enters the character on the training field, where the crawlers are, with the quest stored at the given progress.
    private static ConnectionId EnterOnTheField(TestServer server, long character, int? progress, int level = 1)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, character);
        if (progress != null)
        {
            server.Store.GiveQuest(character, Hunt, progress.Value);
        }

        server.Store.Edit(
            character,
            map: Field,
            position: server.Content.Maps[new MapDefinitionId(Field)].SpawnPosition,
            level: level);
        server.SendEnterWorld(player, character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    private static MapInstance FieldOf(TestServer server)
    {
        return server.World.Maps.Single(map => map.Definition.Id.Value == Field);
    }

    // A live monster of the field dies with the given damage logged; the next tick sends what it changed.
    private static void Kill(TestServer server, string monster, params (ConnectionId Player, int Damage)[] hits)
    {
        MonsterEntity victim = FieldOf(server).Entities.OfType<MonsterEntity>()
            .First(entity => entity.Definition.Id.Value == monster && !entity.IsDead);
        foreach ((ConnectionId player, int damage) in hits)
        {
            victim.LogDamage(server.PlayerOf(player).Character, damage);
        }

        server.Combat.Kill(FieldOf(server), victim, null, server.CurrentTick);
        server.Tick();
    }

    // Each QuestLog the player heard, as "quest state progress/count" lines.
    private static List<string> LogsSentTo(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.QuestLog)
            .Select(message => QuestLog.TryRead(message.Payload, out QuestLog? read) ? read! : null)
            .Select(log => string.Join(
                ", ",
                log!.Entries.Select(entry => $"{entry.Quest.Value} {entry.State} {entry.Progress}/{entry.Count}")))
            .ToList();
    }

    private static string ProgressOf(TestServer server, ConnectionId player)
    {
        CharacterSession character = server.SessionOf(player).Character!;
        return character.Quests.TryGet(new QuestDefinitionId(Hunt), out CharacterQuest? quest)
            ? $"{(quest!.IsCompleted ? "completed" : "active")} {quest.Progress}"
            : "none";
    }

    private static void AssertNotScored(Hunter hunter)
    {
        Assert.That(hunter.Server.SessionOf(hunter.Player).Violations!.Value, Is.Zero, "a refusal is never scored");
    }

    private static IEnumerable<TestCaseData> AcceptRefusals()
    {
        yield return new TestCaseData(Quartermaster, 2f, Hunt, null, false, CommandRejectionReason.InvalidTarget)
            .SetName("Accept at an NPC that does not give it is 1");
        yield return new TestCaseData(
                GateWarden,
                2f,
                "quest.unknown",
                null,
                false,
                CommandRejectionReason.InvalidTarget)
            .SetName("Accept of a quest no NPC gives is 1");
        yield return new TestCaseData(GateWarden, -44f, Hunt, null, false, CommandRejectionReason.InvalidTarget)
            .SetName("Accept from an NPC out of view is 1");
        yield return new TestCaseData(GateWarden, 3.6f, Hunt, null, false, CommandRejectionReason.OutOfRange)
            .SetName("Accept from 3.6 m is 2");
        yield return new TestCaseData(GateWarden, 2f, Hunt, 2, false, CommandRejectionReason.NotAllowedNow)
            .SetName("Accept of an active quest is 3");
        yield return new TestCaseData(GateWarden, 2f, Hunt, 5, true, CommandRejectionReason.NotAllowedNow)
            .SetName("Accept of a completed quest is 3");
    }

    [TestCaseSource(nameof(AcceptRefusals))]
    public void Accept_ThatFailsACheck_IsRefusedWithItsReason_AndChangesNothing(
        string npc,
        float distance,
        string quest,
        int? progress,
        bool isCompleted,
        CommandRejectionReason expected)
    {
        Hunter hunter = AtThe(npc, distance, progress, isCompleted);
        string before = ProgressOf(hunter.Server, hunter.Player);

        hunter.Server.SendAcceptQuest(hunter.Player, hunter.Npc, quest, 1);

        Assert.That(hunter.Answer(), Is.EqualTo(expected));
        Assert.That(ProgressOf(hunter.Server, hunter.Player), Is.EqualTo(before));
        Assert.That(hunter.Logs(), Is.Empty);
        Assert.That(
            hunter.Server.AuditLogger.Entries.Single(entry => entry.EventId.Name == "CommandRefused").Fields["Command"],
            Is.EqualTo(InboundEventKind.AcceptQuest));
        AssertNotScored(hunter);
    }

    // The vectors "Near the cap" and "At the cap": the level stops at the table's cap, where experience stays 0.
    [TestCase(14, 1060L, 15, 0L)]
    [TestCase(15, 0L, 15, 0L)]
    public void TurnIn_NearOrAtTheLevelCap_StopsAtTheCap_AndStillPays(
        int level,
        long experience,
        int expectedLevel,
        long expectedExperience)
    {
        Hunter hunter = AtThe(GateWarden, progress: 5, level: level, experience: experience);

        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);

        Assert.That(hunter.Answer(), Is.EqualTo(CommandRejectionReason.None));
        PlayerEntity player = hunter.Server.PlayerOf(hunter.Player);
        Assert.That((player.Level, player.Experience), Is.EqualTo((expectedLevel, expectedExperience)));
        Assert.That(hunter.Character.Inventory.Coins, Is.EqualTo(100L));
    }

    private static IEnumerable<TestCaseData> TurnInRefusals()
    {
        yield return new TestCaseData(GateWarden, 2f, 4, false, 0L, CommandRejectionReason.NotAllowedNow)
            .SetName("Turn-in short of the count is 3");
        yield return new TestCaseData(GateWarden, 2f, 5, true, 0L, CommandRejectionReason.NotAllowedNow)
            .SetName("Turn-in of a completed quest is 3");
        yield return new TestCaseData(GateWarden, 2f, null, false, 0L, CommandRejectionReason.NotAllowedNow)
            .SetName("Turn-in of a quest never accepted is 3");
        yield return new TestCaseData(GateWarden, 2f, 5, false, Cap - 50, CommandRejectionReason.CoinCapReached)
            .SetName("Turn-in past the coin cap is 11");
        yield return new TestCaseData(GateWarden, 3.6f, 5, false, 0L, CommandRejectionReason.OutOfRange)
            .SetName("Turn-in from 3.6 m is 2");
        yield return new TestCaseData(Quartermaster, 2f, 5, false, 0L, CommandRejectionReason.InvalidTarget)
            .SetName("Turn-in to an NPC that does not give it is 1");
    }

    [TestCaseSource(nameof(TurnInRefusals))]
    public void TurnIn_ThatFailsACheck_IsRefusedWithItsReason_AndChangesNothing(
        string npc,
        float distance,
        int? progress,
        bool isCompleted,
        long coins,
        CommandRejectionReason expected)
    {
        Hunter hunter = AtThe(npc, distance, progress, isCompleted, coins);
        string before = ProgressOf(hunter.Server, hunter.Player);

        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);

        Assert.That(hunter.Answer(), Is.EqualTo(expected));
        Assert.That(ProgressOf(hunter.Server, hunter.Player), Is.EqualTo(before));
        Assert.That(hunter.Server.Store.RewardCommits, Is.Empty);
        Assert.That(hunter.Character.Inventory.Coins, Is.EqualTo(coins));
        Assert.That(
            hunter.Server.AuditLogger.Entries.Single(entry => entry.EventId.Name == "CommandRefused").Fields["Command"],
            Is.EqualTo(InboundEventKind.CompleteQuest));
        AssertNotScored(hunter);
    }

    [Test]
    public void Accept_AtTheGateWarden_AddsTheQuestActive_TellsTheOwner_AndCheckpointsIt()
    {
        Hunter hunter = AtThe(GateWarden);

        hunter.Server.SendAcceptQuest(hunter.Player, hunter.Npc, Hunt, 1);
        CommandRejectionReason answer = hunter.Answer();

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(hunter.Logs(), Is.EqualTo(new[] { $"{Hunt} Active 0/5" }));
        Assert.That(
            hunter.Server.Store.Stored(1).Quests
                .Select(quest => (quest.QuestDefinitionId, quest.IsCompleted, quest.Progress)),
            Is.EqualTo(new[] { (Hunt, false, 0) }),
            "the acceptance is checkpointed");
        Assert.That(
            hunter.Server.ProgressionLog.Entries.Single(entry => entry.EventId.Name == "QuestAccepted").Fields["Quest"],
            Is.EqualTo(Hunt));
    }

    // The vector "A later checkpoint": taken while the turn-in is in flight, a checkpoint leaves the level and
    // experience to the turn-in, which commits the reward's pair; memory then holds the reward on top of what came
    // meanwhile, for the checkpoint queued once the turn-in is over to write.
    [Test]
    public void Checkpoint_WhileATurnInIsInFlight_LeavesTheLevelAndExperienceToTheReward()
    {
        Hunter hunter = AtThe(GateWarden, progress: 5);
        hunter.Server.RunsPersistence = false;
        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);
        hunter.Server.Tick();
        hunter.Character.Player.Experience = 25;
        hunter.Server.Lifetime.QueueCheckpoint(hunter.Character);

        hunter.Server.RunsPersistence = true;
        hunter.Server.TickUntil(() => hunter.Character.Operation == null);

        CharacterCheckpoint during = hunter.Server.Store.Checkpoints.First();
        Assert.That((during.IsRewardInFlight, during.Experience), Is.EqualTo((true, 25L)));
        StoredCharacter stored = hunter.Server.Store.Stored(1);
        Assert.That((stored.BaseLevel, stored.Experience), Is.EqualTo((3, 70L)), "the reward's pair, durable");
        PlayerEntity player = hunter.Character.Player;
        Assert.That((player.Level, player.Experience), Is.EqualTo((4, 15L)), "25 + 150 = 175 in memory");
    }

    [Test]
    public void Enter_WithAStoredQuestTheContentLacks_IsRefused_AndTheRowIsKept()
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveQuest(1, "quest.retired", 1);

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State != SessionState.EnteringWorld);

        Assert.That(server.SessionOf(player).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(
            server.Log.Entries
                .Where(entry => entry.EventId.Name == "CharacterContentMismatch")
                .Select(entry => entry.Message),
            Has.Some.Contains("quest 'quest.retired'"));
        Assert.That(server.Store.Stored(1).Quests.Single().QuestDefinitionId, Is.EqualTo("quest.retired"));
    }

    [Test]
    public void Enter_WithStoredQuests_ListsThemLastInTheBaseline()
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveQuest(1, Hunt, 3);

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);

        Assert.That(server.Transport.ControlOpcodesSentTo(player).Last(), Is.EqualTo(MessageOpcode.QuestLog));
        Assert.That(LogsSentTo(server, player), Is.EqualTo(new[] { $"{Hunt} Active 3/5" }));
    }

    // Kills count for those who may share the experience (Gameplay Systems §2.1): a character in its reconnect grace
    // period is still on the map, and one logging out is not.
    [Test]
    public void Kill_CountsForAHolderInItsGracePeriod_ButNotForOneLoggingOut()
    {
        TestServer server = FieldServer(30_000);
        ConnectionId away = EnterOnTheField(server, 1, 2);
        ConnectionId leaving = EnterOnTheField(server, 2, 2);
        CharacterSession retained = server.SessionOf(away).Character!;
        CharacterSession loggingOut = server.SessionOf(leaving).Character!;
        server.Disconnect(away);
        server.RunsPersistence = false;
        server.SendLogout(leaving, 1);
        server.Tick(2);
        Assert.That((retained.Connection, loggingOut.IsLoggingOut), Is.EqualTo(((ClientSession?)null, true)));
        MonsterEntity victim = FieldOf(server).Entities.OfType<MonsterEntity>()
            .First(entity => entity.Definition.Id.Value == Crawler && !entity.IsDead);
        victim.LogDamage(retained.Character, 30);
        victim.LogDamage(loggingOut.Character, 30);

        server.Combat.Kill(FieldOf(server), victim, null, server.CurrentTick);

        Assert.That(
            (retained.Quests.Entries.Single().Progress, loggingOut.Quests.Entries.Single().Progress),
            Is.EqualTo((3, 2)));
    }

    [Test]
    public void Kill_OfACrawler_CountsOneForEveryHolderThatHitIt_WhateverTheSplit()
    {
        TestServer server = FieldServer();
        ConnectionId first = EnterOnTheField(server, 1, 2);
        ConnectionId second = EnterOnTheField(server, 2, 2);
        server.Transport.ClearSent();

        Kill(server, Crawler, (first, 90), (second, 10));

        Assert.That(
            (ProgressOf(server, first), ProgressOf(server, second)),
            Is.EqualTo(("active 3", "active 3")));
        Assert.That(LogsSentTo(server, first), Is.EqualTo(new[] { $"{Hunt} Active 3/5" }));
        Assert.That(LogsSentTo(server, second), Is.EqualTo(new[] { $"{Hunt} Active 3/5" }));
    }

    [Test]
    public void Kill_PastTheCount_OfAnotherMonster_OrUnhitByTheHolder_CountsNothing()
    {
        TestServer server = FieldServer();
        ConnectionId ready = EnterOnTheField(server, 1, 5);
        ConnectionId holder = EnterOnTheField(server, 2, 2);
        ConnectionId other = EnterOnTheField(server, 3, null);
        server.Transport.ClearSent();

        Kill(server, Crawler, (ready, 30));
        Kill(server, Wisp, (holder, 30));
        Kill(server, Crawler, (other, 30));

        Assert.That(
            (ProgressOf(server, ready), ProgressOf(server, holder), ProgressOf(server, other)),
            Is.EqualTo(("active 5", "active 2", "none")),
            "a kill without the quest adds none either");
        Assert.That(LogsSentTo(server, ready).Concat(LogsSentTo(server, holder)), Is.Empty);
    }

    [Test]
    public void Kill_ThatReachesTheCount_MakesTheQuestReady_AndCheckpointsIt()
    {
        TestServer server = FieldServer();
        ConnectionId player = EnterOnTheField(server, 1, 4);
        server.Transport.ClearSent();

        Kill(server, Crawler, (player, 30));
        server.Tick();

        Assert.That(ProgressOf(server, player), Is.EqualTo("active 5"));
        Assert.That(LogsSentTo(server, player), Is.EqualTo(new[] { $"{Hunt} Active 5/5" }));
        Assert.That(server.Store.Stored(1).Quests.Single().Progress, Is.EqualTo(5), "the ready quest is checkpointed");
    }

    // The vector "A crash": progress is checkpointed like experience, so what came after the last checkpoint is lost,
    // while the acceptance is not. At level 5 two crawlers bring no level-up, whose checkpoint would hold the progress.
    [Test]
    public void Progress_AfterTheLastCheckpoint_IsLostWithTheServer_ButTheAcceptanceIsNot()
    {
        TestServer server = FieldServer();
        ConnectionId player = EnterOnTheField(server, 1, 1, 5);
        Kill(server, Crawler, (player, 30));
        Kill(server, Crawler, (player, 30));
        Assert.That(ProgressOf(server, player), Is.EqualTo("active 3"));

        var restarted = new TestServer(
            withMonsters: true,
            withMonsterAi: false,
            withEveryMap: true,
            store: server.Store);
        ConnectionId again = restarted.Connect();
        restarted.SignInWithCharacter(again, 1);
        restarted.SendEnterWorld(again, 1);
        restarted.TickUntil(() => restarted.SessionOf(again).State == SessionState.InWorld);

        Assert.That(ProgressOf(restarted, again), Is.EqualTo("active 1"));
    }

    // The reward's 150 job experience from job level 1: 30 and 50 reach job level 3, and 70 of the 80 job level 4
    // needs are left. The turn-in's commit carries the job pair, and the award after it gives it once in memory
    // (Gameplay Systems §2.2).
    [Test]
    public void TurnIn_CommitsTheRewardsJobExperienceWithIt_AndAwardsItOnce()
    {
        Hunter hunter = AtThe(
            GateWarden,
            progress: 5,
            prepare: store => store.Edit(1, jobLevel: 1, skills: new Dictionary<string, int>()));

        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);

        Assert.That(hunter.Answer(), Is.EqualTo(CommandRejectionReason.None));
        StoredCharacter stored = hunter.Server.Store.Stored(1);
        PlayerEntity player = hunter.Server.PlayerOf(hunter.Player);
        Assert.That((stored.JobLevel, stored.JobExperience), Is.EqualTo((3, 70L)), "committed with the reward");
        Assert.That((player.JobLevel, player.JobExperience), Is.EqualTo((3, 70L)), "awarded once, in memory");
    }

    // The vector "Death before the answer": the reward keeps its coins and levels, and a level-up never revives.
    [Test]
    public void TurnIn_OfACharacterThatDiesBeforeTheAnswer_KeepsTheReward_AndStaysDead()
    {
        Hunter hunter = AtThe(GateWarden, progress: 5);
        hunter.Server.RunsPersistence = false;
        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);
        hunter.Server.Tick();
        PlayerEntity player = hunter.Server.PlayerOf(hunter.Player);
        hunter.Server.Combat.Kill(hunter.Character.Map, player, null, hunter.Server.CurrentTick);

        hunter.Server.RunsPersistence = true;
        hunter.Server.TickUntil(() => hunter.Character.Operation == null);

        Assert.That((player.Level, hunter.Character.Inventory.Coins), Is.EqualTo((3, 100L)));
        Assert.That((player.IsDead, player.CurrentHealth), Is.EqualTo((true, 0)), "still dead");
    }

    [Test]
    public void TurnIn_OfAReadyQuest_PaysTheRewardAfterItsCommit_AndCompletesTheQuest()
    {
        Hunter hunter = AtThe(GateWarden, progress: 5);

        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);
        CommandRejectionReason answer = hunter.Answer();

        PlayerEntity player = hunter.Server.PlayerOf(hunter.Player);
        InventoryChanged change = hunter.Server.Transport.ControlSentTo(hunter.Player)
            .Where(message => message.Opcode == MessageOpcode.InventoryChanged)
            .Select(message => InventoryChanged.TryRead(message.Payload, out InventoryChanged? read) ? read! : null)
            .Single()!;
        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That((change.Coins, change.Changes.Count), Is.EqualTo((100u, 0)), "the coins, and no row");
        Assert.That(hunter.Logs(), Is.EqualTo(new[] { $"{Hunt} Completed 5/5" }));
        Assert.That((player.Level, player.Experience), Is.EqualTo((3, 70L)), "150 base experience through two levels");
        Assert.That(player.CurrentHealth, Is.EqualTo(player.MaxHealth), "a level-up fills a live character");
        StoredCharacter stored = hunter.Server.Store.Stored(1);
        Assert.That(
            (stored.Coins, stored.BaseLevel, stored.Experience, stored.Quests.Single().IsCompleted),
            Is.EqualTo((100L, 3, 70L, true)));
        Assert.That(hunter.Server.Store.RewardCommits, Has.Count.EqualTo(1));
        IReadOnlyDictionary<string, object?> fields = hunter.Server.ProgressionLog.Entries
            .Single(entry => entry.EventId.Name == "QuestCompleted")
            .Fields;
        Assert.That(
            (fields["Quest"], fields["Experience"], fields["JobExperience"], fields["Coins"]),
            Is.EqualTo(((object?)Hunt, (object?)150L, (object?)150L, (object?)100L)));
    }

    // The server's copy said ready and the database said completed: that copy is out of date, which nothing the player
    // sent could cause, so the answer is 1, as for the items of Milestone 6.
    [Test]
    public void TurnIn_ThatTheStoreRefuses_IsAnsweredOne_AndAuditedAsATurnIn()
    {
        Hunter hunter = AtThe(GateWarden, progress: 5);
        hunter.Server.Store.GiveQuest(1, Hunt, 5, true);

        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);

        Assert.That(hunter.Answer(), Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(hunter.Server.Store.RewardCommits, Has.Count.EqualTo(1));
        Assert.That(hunter.Character.Inventory.Coins, Is.Zero);
        Assert.That(ProgressOf(hunter.Server, hunter.Player), Is.EqualTo("active 5"));
        Assert.That(
            hunter.Server.AuditLogger.Entries.Single(entry => entry.EventId.Name == "CommandRefused").Fields["Command"],
            Is.EqualTo(InboundEventKind.CompleteQuest));
        AssertNotScored(hunter);
    }

    [Test]
    public void TurnIn_WhileAnotherInventoryOperationIsInFlight_IsReason9()
    {
        Hunter hunter = AtThe(GateWarden, progress: 5, prepare: store => store.GiveItems(1, Potion, 1, 3, 0));
        long potion = hunter.Server.Store.Stored(1).Items.Single().Id;
        hunter.Server.RunsPersistence = false;

        hunter.Server.SendUseItem(hunter.Player, potion, 1);
        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 2);
        hunter.Server.Tick();

        CommandRejected refused = hunter.Server.Transport.ControlSentTo(hunter.Player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
            .Single();
        Assert.That(
            (refused.CommandSequence, refused.Reason),
            Is.EqualTo((2u, CommandRejectionReason.ItemActionInFlight)));
        AssertNotScored(hunter);
    }

    [Test]
    public void TurnIn_WhoseAnswerIsLost_IsSettledFromTheLedger_AndPaysOnce()
    {
        Hunter hunter = AtThe(GateWarden, progress: 5);
        hunter.Server.Store.AmbiguousRewardFailures = 100;
        int ledger = hunter.Server.Store.LedgerCount;

        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);
        hunter.Server.Tick(2);
        bool isUnsettled = hunter.Character.Operation != null;
        hunter.Server.Store.AmbiguousRewardFailures = 0;
        hunter.Server.TickUntil(() => hunter.Character.Operation == null);

        Assert.That(isUnsettled, Is.True, "no answer yet");
        Assert.That(hunter.Server.Store.LedgerCount, Is.EqualTo(ledger + 1), "committed once");
        Assert.That(hunter.Server.Store.Stored(1).Coins, Is.EqualTo(100L), "paid once");
        Assert.That((hunter.Character.Inventory.Coins, hunter.Character.Player.Level), Is.EqualTo((100L, 3)));
        Assert.That(hunter.Logs(), Is.EqualTo(new[] { $"{Hunt} Completed 5/5" }));
        Assert.That(
            hunter.Server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "InventoryOperationUnsettled")
                .Fields["Kind"],
            Is.EqualTo(InventoryOperationKind.QuestReward));
    }

    // A kill while a turn-in is in flight adds experience its commit cannot carry, and the checkpoints of the flight
    // leave the level and experience to the turn-in; once it is over, what the character holds is saved at once.
    [Test]
    public void TurnIn_WithExperienceEarnedInFlight_SavesWhatTheCharacterHolds_OnceItIsOver()
    {
        Hunter hunter = AtThe(GateWarden, progress: 5);
        hunter.Server.RunsPersistence = false;
        hunter.Server.SendCompleteQuest(hunter.Player, hunter.Npc, Hunt, 1);
        hunter.Server.Tick();

        // As a kill during the flight would.
        hunter.Character.Player.Experience += 25;
        hunter.Server.RunsPersistence = true;
        hunter.Server.TickUntil(() => hunter.Character.Operation == null);
        hunter.Server.Tick(3);

        StoredCharacter stored = hunter.Server.Store.Stored(1);
        Assert.That(
            (hunter.Character.Player.Level, hunter.Character.Player.Experience),
            Is.EqualTo((4, 15L)),
            "25 + 150 = 175 in memory");
        Assert.That((stored.BaseLevel, stored.Experience), Is.EqualTo((4, 15L)), "saved once the turn-in was over");
    }
}
}
