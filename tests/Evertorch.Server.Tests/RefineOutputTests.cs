using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What refining sends (Network Protocol §9): the refiner alone hears its bag's change and the outcome, and no new
///     sheet, since a worn row never refines; an observer beside it hears nothing of it; the tables reach whoever sees
///     the Smith. And the draws replay: one seed gives the same outcomes, which past the safe level are both a raise and
///     a destroyed item.
/// </summary>
[TestFixture]
public sealed class RefineOutputTests
{
    private const string Smith = "npc.smith";
    private const string Sword = "item.weapon.training_sword";
    private const string Ember = "item.material.emberstone";
    private const long Refiner = 1;
    private const long Observer = 2;

    private static ConnectionId EnterBesideTheSmith(TestServer server, long character, int swords, int level)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, character);
        if (swords > 0)
        {
            server.Store.GiveItems(character, Sword, swords, 1, 5);
            foreach (StoredItem row in server.Store.Stored(character).Items
                         .Where(item => item.ItemDefinitionId == Sword))
            {
                server.Store.Refine(character, row.Id, level);
            }

            server.Store.GiveItems(character, Ember, 1, swords, 5);
            server.Store.Edit(character, coins: 20L * swords);
        }

        server.SendEnterWorld(player, character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity smith = server.NpcOf(Smith);
        server.Place(player, smith.Position.X + 2f, smith.Position.Z + character - 1);
        server.Tick(2);
        return player;
    }

    // Each sword at +7 is refined once, one after another; the outcomes in order.
    private static List<RefineOutcomeKind> RefineEach(TestServer server, ConnectionId player)
    {
        CharacterSession character = server.SessionOf(player).Character!;
        EntityId smith = server.NpcOf(Smith).Id;
        long[] swords = character.Inventory.Rows.Where(row => row.Item.Value == Sword)
            .Select(row => row.InventoryItem)
            .ToArray();
        uint sequence = 1;
        foreach (long sword in swords)
        {
            server.SendRefine(player, smith, sword, 7, sequence++);
            server.Tick();
            server.TickUntil(() => character.Operation == null && server.Persistence.PendingJobs == 0);
        }

        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.RefineOutcome)
            .Select(message => RefineOutcome.TryRead(message.Payload, out RefineOutcome? read) ? read!.Outcome : 0)
            .ToList();
    }

    [Test]
    public void ARefine_ReachesItsRefinerAlone_AndAnObserverAsNothing_WhileBothHearTheTables()
    {
        var server = new TestServer(withNpcs: true, refineRandom: new ScriptedRandom(0));
        ConnectionId refiner = EnterBesideTheSmith(server, Refiner, 1, 0);
        ConnectionId observer = EnterBesideTheSmith(server, Observer, 0, 0);
        int tables = server.Transport.ControlSentTo(observer)
            .Count(message => message.Opcode == MessageOpcode.RefineTable);
        server.Transport.ClearSent();
        CharacterSession character = server.SessionOf(refiner).Character!;
        long sword = character.Inventory.Rows.Single(row => row.Item.Value == Sword).InventoryItem;

        server.SendRefine(refiner, server.NpcOf(Smith).Id, sword, 0, 1);
        server.Tick();
        server.TickUntil(() => character.Operation == null && server.Persistence.PendingJobs == 0);
        server.Tick(2);

        MessageOpcode[] refinerHeard = server.Transport.SentTo(refiner).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(tables, Is.EqualTo(4), "the observer sees the Smith and heard its tables");
        Assert.That(
            refinerHeard.Where(opcode => opcode != MessageOpcode.EntitySnapshot),
            Is.EqualTo(new[] { MessageOpcode.InventoryChanged, MessageOpcode.RefineOutcome }),
            "the bag and the outcome, and no sheet: nothing worn changed");
        Assert.That(
            observerHeard.Where(opcode => opcode != MessageOpcode.EntitySnapshot),
            Is.Empty,
            "nothing of another's refine");
    }

    // One seed, the same commands, the same outcomes (Gameplay Systems §11.6); twelve swords at +7, where the training
    // table succeeds at 60 %, see both a raise and a destroyed item.
    [Test]
    public void Refines_UnderOneSeed_Replay_AndPastTheSafeLevelBothRaiseAndDestroy()
    {
        var first = new TestServer(withNpcs: true, randomSeed: 16);
        var second = new TestServer(withNpcs: true, randomSeed: 16);

        List<RefineOutcomeKind> outcomes = RefineEach(first, EnterBesideTheSmith(first, Refiner, 12, 7));
        List<RefineOutcomeKind> replayed = RefineEach(second, EnterBesideTheSmith(second, Refiner, 12, 7));

        Assert.That(outcomes, Has.Count.EqualTo(12));
        Assert.That(replayed, Is.EqualTo(outcomes), "the same draws");
        Assert.That(outcomes, Does.Contain(RefineOutcomeKind.Refined).And.Contain(RefineOutcomeKind.Destroyed));
    }
}
}
