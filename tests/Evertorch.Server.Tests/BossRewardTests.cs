using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The grant of a boss's prize (Gameplay Systems §11; Persistence §5; owner decision 8): an inventory operation the
///     server starts on its own, one at a time and only when nothing else of the character's inventory is in flight,
///     which every inventory command and a logout wait for; a full bag leaves the prize at the MVP's feet under the
///     grant's operation ID, the MVP's alone for 10 s; a lost answer is settled from the ledger; and an outage never
///     leaves it on the ground.
/// </summary>
[TestFixture]
public sealed class BossRewardTests
{
    private const string Mantle = "item.armor.monarch_mantle";
    private const string Gel = "item.material.slime_gel";

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    // The scripted drops' first draw keeps the Mantle; the AI is off.
    private static TestServer InTheGrotto()
    {
        return new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            dropRandom: new ScriptedRandom(0));
    }

    private static ConnectionId Enter(TestServer server, long character, int fullRows = 0)
    {
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, character);
        if (fullRows > 0)
        {
            server.Store.GiveItems(character, Gel, fullRows, 1, 0);
        }

        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId connection)
    {
        return server.SessionOf(connection).Character!;
    }

    // The player's hits make it the boss's MVP; the boss dies at once.
    private static void Win(TestServer server, ConnectionId connection)
    {
        PlayerEntity player = server.PlayerOf(connection);
        MonsterEntity monarch = GrottoOf(server).Monsters.Single(monster => monster.Definition.Id == Monarch);
        monarch.LogMvpDealt(player.Character, 500);
        server.Combat.Kill(GrottoOf(server), monarch, player, server.CurrentTick);
    }

    private static void Grant(CharacterSession character)
    {
        character.PendingGrants.Enqueue(new BossGrant(Monarch, new ItemDefinitionId(Mantle), 1, 3000));
    }

    private static void Settle(TestServer server, CharacterSession character)
    {
        for (int tick = 0; tick < 5 * TestServer.TickRate && character.HasInventoryWork; tick++)
        {
            server.Tick();
        }

        Assert.That(character.HasInventoryWork, Is.False, "the grant settled");
    }

    // Until a pickup sent now is applied at least ms after the drop appeared.
    private static void TickUntilAged(TestServer server, ItemDropEntity drop, int ms)
    {
        while ((server.CurrentTick - drop.DroppedTick) * 1000L / TestServer.TickRate < ms)
        {
            server.Tick();
        }
    }

    private static int Mantles(CharacterSession character)
    {
        return character.Inventory.Rows.Count(row => row.Item.Value == Mantle);
    }

    private static IEnumerable<ItemDropEntity> MantlesOnTheGround(TestServer server)
    {
        return server.World.Maps.SelectMany(map => map.ItemDrops).Where(drop => drop.Item.Value == Mantle);
    }

    private static IEnumerable<int> RewardEvents(TestServer server)
    {
        return server.RewardLog.Entries.Select(entry => entry.EventId.Id);
    }

    private static List<MvpAwarded> Awards(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.MvpAwarded)
            .Select(message =>
            {
                Assert.That(MvpAwarded.TryRead(message.Payload, out MvpAwarded? award), Is.True);
                return award!;
            })
            .ToList();
    }

    private static List<CommandRejected> Rejections(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToList();
    }

    // A boss that gives its MVP no prize tells the award at once, with no item.
    [Test]
    public void Fall_WithAnMvpButNoPrize_TellsTheAwardAtOnce_ToTheMvpAlone()
    {
        TestServer server = InTheGrotto();
        ConnectionId connection = Enter(server, 1);
        ConnectionId other = Enter(server, 2);
        server.CrossIntoTheGrotto(connection);
        server.CrossIntoTheGrotto(other);
        CharacterSession character = CharacterOf(server, connection);
        MonsterEntity monarch = GrottoOf(server).Monsters.Single(monster => monster.Definition.Id == Monarch);
        server.Transport.ClearSent();

        server.Bosses.Fell(GrottoOf(server), monarch, new MostValuablePlayer(character, 3000, null));

        MvpAwarded award = Awards(server, connection).Single();
        Assert.That(
            (award.Monster, award.MvpExperience, award.Item, award.Amount, award.Placed),
            Is.EqualTo((Monarch, 3000UL, (ItemDefinitionId?)null, 0U, PrizePlacement.None)));
        Assert.That(Awards(server, other), Is.Empty);
        Assert.That(character.HasInventoryWork, Is.False);
    }

    // The outage cuts the commit short; once the database answers, the ledger shows it never happened, and the grant
    // is made again under the same operation ID rather than dropped.
    [Test]
    public void Grant_InAnOutage_Waits_AndIsMadeAgain_NeverLeftOnTheGround()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        CharacterSession character = CharacterOf(server, connection);
        server.Store.IsUnavailable = true;

        Grant(character);
        server.Tick(2 * TestServer.TickRate);
        int waiting = character.PendingGrants.Count;
        bool isOnTheGround = MantlesOnTheGround(server).Any();
        server.Store.IsUnavailable = false;
        Settle(server, character);

        Assert.That((waiting, isOnTheGround), Is.EqualTo((1, false)), "held through the outage");
        Assert.That(server.Store.Lookups, Is.EqualTo(new[] { server.Store.GrantCommits.Single().OperationId }));
        Assert.That(Mantles(character), Is.EqualTo(1));
        Assert.That(server.Store.Stored(1).Items.Count(item => item.ItemDefinitionId == Mantle), Is.EqualTo(1));
        Assert.That(MantlesOnTheGround(server), Is.Empty);
        Assert.That(RewardEvents(server), Is.EqualTo(new[] { 4012, 1025 }));
    }

    [Test]
    public void Grant_Pending_HoldsTheMvpOnAPortal_UntilItSettles()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId connection = server.EnterWorld(1);
        CharacterSession character = CharacterOf(server, connection);
        MapPortal portal = character.Map.Definition.Portals
            .Single(gate => gate.DestinationMap == new MapDefinitionId("map.training_field"));
        server.RunsPersistence = false;

        Grant(character);
        server.PlayerOf(connection).Position = portal.Center;
        server.Tick(3);
        MapDefinitionId meanwhile = character.Map.Definition.Id;
        server.RunsPersistence = true;
        Settle(server, character);
        server.Tick();

        Assert.That(meanwhile.Value, Is.EqualTo("map.training_ground"), "the prize kept it from crossing");
        Assert.That(character.Map.Definition.Id.Value, Is.EqualTo("map.training_field"), "once the prize settled");
        Assert.That(Mantles(character), Is.EqualTo(1));
    }

    // A grant not yet started refuses an inventory command as surely as one in flight; the logout's checkpoint waits for
    // the grant, so the prize reaches the bag before the character leaves.
    [Test]
    public void Grant_Pending_RefusesAnItemCommandAsAChangeStillGoingThrough_AndALogoutWaitsForIt()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        CharacterSession character = CharacterOf(server, connection);
        server.RunsPersistence = false;
        server.Transport.ClearSent();

        Grant(character);
        server.SendBuy(connection, new EntityId(999999), "item.consumable.minor_health", 1, 1);
        server.Tick();
        CommandRejectionReason refused = Rejections(server, connection).Single().Reason;
        server.SendLogout(connection, 2);
        server.Tick(3);
        bool isCheckpointQueued = character.LogoutCheckpoint != null;
        server.RunsPersistence = true;
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.Authenticated);

        MessageOpcode[] opcodes = server.Transport.ControlOpcodesSentTo(connection).ToArray();
        Assert.That(refused, Is.EqualTo(CommandRejectionReason.ItemActionInFlight));
        Assert.That(isCheckpointQueued, Is.False, "the logout's checkpoint waits for the prize");
        Assert.That(
            Array.IndexOf(opcodes, MessageOpcode.InventoryChanged),
            Is.GreaterThanOrEqualTo(0).And.LessThan(Array.IndexOf(opcodes, MessageOpcode.LogoutComplete)));
        Assert.That(server.Store.Stored(1).Items.Count(item => item.ItemDefinitionId == Mantle), Is.EqualTo(1));
    }

    // A grant not yet started when its player leaves still starts, and the character is removed only after it.
    [Test]
    public void Grant_Pending_WhenTheMvpDisconnects_ReachesTheBag_ThenTheCharacterIsRemoved()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        CharacterSession character = CharacterOf(server, connection);
        server.RunsPersistence = false;

        Grant(character);
        server.Disconnect(connection);
        server.Tick(2);
        InventoryOperationKind? meanwhile = character.Operation?.Kind;
        int players = server.World.Maps.Single().Players.Count;
        server.RunsPersistence = true;
        server.Tick(3);

        Assert.That((meanwhile, players), Is.EqualTo((InventoryOperationKind.BossReward, 1)),
            "started, and waited for");
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.Store.Stored(1).Items.Count(item => item.ItemDefinitionId == Mantle), Is.EqualTo(1));
        Assert.That(server.Store.Checkpoints, Has.Count.EqualTo(1), "the removal's checkpoint, after the prize");
    }

    [Test]
    public void Grant_WaitsForAnOperationInFlight_ThenFollowsIt()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        CharacterSession character = CharacterOf(server, connection);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            character.Map,
            new ItemDefinitionId(Gel),
            1,
            character.Player.Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.RunsPersistence = false;
        server.SendPickup(connection, drop.Id, 1);
        server.Tick();

        Grant(character);
        server.Tick(3);
        InventoryOperationKind? meanwhile = character.Operation?.Kind;
        server.RunsPersistence = true;
        Settle(server, character);

        Assert.That(meanwhile, Is.EqualTo(InventoryOperationKind.Pickup), "the pickup kept the bag");
        Assert.That((server.Store.PickupCommits.Count, server.Store.GrantCommits.Count), Is.EqualTo((1, 1)));
        Assert.That(Mantles(character), Is.EqualTo(1));
    }

    [Test]
    public void Grant_WhoseAnswerWasLost_IsSettledFromTheLedger()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        CharacterSession character = CharacterOf(server, connection);
        server.Store.AmbiguousGrantFailures = 100;

        Grant(character);
        Settle(server, character);

        Assert.That(server.Store.Lookups, Is.EqualTo(new[] { server.Store.GrantCommits[0].OperationId }));
        Assert.That(Mantles(character), Is.EqualTo(1));
        Assert.That(server.Store.Stored(1).Items.Count(item => item.ItemDefinitionId == Mantle), Is.EqualTo(1));
        Assert.That(RewardEvents(server), Is.EqualTo(new[] { 4012, 1025 }));
        Assert.That(MantlesOnTheGround(server), Is.Empty);
    }

    // The writer's own retry replays the commit whose answer it lost; the ledger answers the replay with the first.
    [Test]
    public void Grant_WhoseCommitIsReplayed_IsCommittedOnce()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        CharacterSession character = CharacterOf(server, connection);
        server.Store.AmbiguousGrantFailures = 1;

        Grant(character);
        Settle(server, character);

        Assert.That(server.Store.GrantCommits.Select(commit => commit.OperationId).Distinct().Count(), Is.EqualTo(1));
        Assert.That(server.Store.GrantCommits, Has.Count.EqualTo(2), "the commit and its replay");
        Assert.That(Mantles(character), Is.EqualTo(1));
        Assert.That(server.Store.Stored(1).Items.Count(item => item.ItemDefinitionId == Mantle), Is.EqualTo(1));
        Assert.That(RewardEvents(server), Is.EqualTo(new[] { 1025 }));
    }

    [Test]
    public void Kill_OfTheBoss_TakesThePrizeIntoTheMvpsBag_InOneCommit_AndTellsItsOwner()
    {
        TestServer server = InTheGrotto();
        ConnectionId connection = Enter(server, 1);
        ConnectionId other = Enter(server, 2);
        server.CrossIntoTheGrotto(connection);
        server.CrossIntoTheGrotto(other);
        CharacterSession character = CharacterOf(server, connection);
        server.Transport.ClearSent();

        Win(server, connection);
        bool isWaiting = character.HasInventoryWork;
        bool isToldEarly = Awards(server, connection).Count > 0;
        Settle(server, character);

        MvpAwarded award = Awards(server, connection).Single();
        var opcodes = server.Transport.ControlOpcodesSentTo(connection).ToList();
        Assert.That((isWaiting, isToldEarly), Is.EqualTo((true, false)), "the prize waited, and its award with it");
        Assert.That(server.Store.GrantCommits.Select(commit => commit.ItemDefinitionId), Is.EqualTo(new[] { Mantle }));
        Assert.That(Mantles(character), Is.EqualTo(1));
        Assert.That(server.Store.Stored(1).Items.Count(item => item.ItemDefinitionId == Mantle), Is.EqualTo(1));
        Assert.That(
            (award.Monster, award.MvpExperience, award.Item, award.Amount, award.Placed),
            Is.EqualTo((Monarch, 3000UL, (ItemDefinitionId?)new ItemDefinitionId(Mantle), 1U, PrizePlacement.Bag)));
        Assert.That(
            opcodes.IndexOf(MessageOpcode.InventoryChanged),
            Is.GreaterThanOrEqualTo(0).And.LessThan(opcodes.IndexOf(MessageOpcode.MvpAwarded)),
            "the bag changes before the award is told");
        Assert.That(Awards(server, other), Is.Empty, "the MVP alone hears it");
        Assert.That(RewardEvents(server), Is.EqualTo(new[] { 1025 }));
        Assert.That(MantlesOnTheGround(server), Is.Empty);
    }

    // At 5 s another player is still refused, as no kill's loot would be; at 10 s the prize is anyone's, and the
    // ledger lets it into that one bag under the grant's ID.
    [Test]
    public void Kill_OfTheBoss_WithTheMvpsBagFull_LeavesThePrizeAtItsFeet_UnderTheGrantsId_ItsAloneForTenSeconds()
    {
        TestServer server = InTheGrotto();
        ConnectionId winner = Enter(server, 1, PickupSystem.MaxInventoryRows);
        ConnectionId other = Enter(server, 2);
        server.CrossIntoTheGrotto(winner);
        server.CrossIntoTheGrotto(other);
        server.PlayerOf(other).Position = server.PlayerOf(winner).Position;
        CharacterSession character = CharacterOf(server, winner);

        Win(server, winner);
        Settle(server, character);
        uint placed = server.CurrentTick;
        ItemDropEntity prize = MantlesOnTheGround(server).Single();
        Guid grant = server.Store.GrantCommits.Single().OperationId;
        PrizePlacement told = Awards(server, winner).Single().Placed;
        TickUntilAged(server, prize, 5000);
        server.Transport.ClearSent();
        server.SendPickup(other, prize.Id, 1);
        server.Tick();
        CommandRejectionReason atFiveSeconds = Rejections(server, other).Single().Reason;
        TickUntilAged(server, prize, BossRewardSystem.PrizePriorityMs);
        server.SendPickup(other, prize.Id, 2);
        server.Tick(3);

        Assert.That((prize.DropId, prize.Priority), Is.EqualTo((grant, character.Character)));
        Assert.That(prize.DroppedTick, Is.EqualTo(placed), "its window counts from the tick it appeared");
        Assert.That(told, Is.EqualTo(PrizePlacement.Feet), "the award says where the prize lies");
        Assert.That(prize.Position, Is.EqualTo(server.PlayerOf(winner).Position), "at the MVP's feet");
        Assert.That(atFiveSeconds, Is.EqualTo(CommandRejectionReason.LootPriority));
        Assert.That(Mantles(CharacterOf(server, other)), Is.EqualTo(1), "anyone's at 10 s");
        Assert.That(Mantles(character), Is.Zero);
        Assert.That(RewardEvents(server), Is.EqualTo(new[] { 1026 }));
    }
}
}
