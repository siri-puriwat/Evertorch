using System.Collections.Generic;
using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The cards at their limits (Network Protocol §6, §11; Gameplay Systems §11.7): a bag of 100 carded rows reaches its
///     owner in parts within the reliable bound that assemble whole, and a removal from it, with no stack of the card,
///     is refused with 5; a card or an item from another character's bag is refused with 1.
/// </summary>
[TestFixture]
public sealed class CardHardeningTests
{
    private const string Smith = "npc.smith";
    private const string Sword = "item.weapon.iron_sword";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";

    private static CommandRejectionReason Answer(TestServer server, ConnectionId player)
    {
        CharacterSession character = server.SessionOf(player).Character!;
        server.Tick();
        server.TickUntil(() => character.Operation == null && server.Persistence.PendingJobs == 0);
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Last();
    }

    // Character 2's card or sword, named by its row, is no row of character 1's.
    [TestCase(true)]
    [TestCase(false)]
    public void Insert_OfAnotherCharactersRow_IsRefusedWithOne(bool isTheCardTheirs)
    {
        var server = new TestServer();
        var rows = new Dictionary<(long, string), long>();
        foreach (long character in new long[] { 1, 2 })
        {
            ConnectionId connection = server.Connect();
            server.SignInWithCharacter(connection, character);
            foreach (string item in new[] { Keen, Sword })
            {
                server.Store.GiveItems(character, item, 1, 1, 5);
                rows[(character, item)] = server.Store.Stored(character).Items.Last().Id;
            }

            server.Disconnect(connection);
            server.Tick();
        }

        ConnectionId player = server.EnterWorld(1);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendInsertCard(
            player,
            rows[(isTheCardTheirs ? 2 : 1, Keen)],
            rows[(isTheCardTheirs ? 1 : 2, Sword)],
            1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(server.Store.CardCommits, Is.Empty);
    }

    [Test]
    public void AHundredCardedRows_ArriveInPartsWithinTheBound_AndARemovalWithoutRoomIsRefusedWithFive()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, Sword, 100, 1, 5);
        foreach (long row in server.Store.Stored(1).Items.Select(item => item.Id).ToArray())
        {
            server.Store.Card(1, row, Keen, Bane);
        }

        server.Store.Edit(1, coins: 1000);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity smith = server.NpcOf(Smith);
        server.Place(player, smith.Position.X + 2f, smith.Position.Z);
        server.Tick(2);
        var parts = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.InventorySnapshot)
            .Select(message => message.Payload)
            .ToList();
        var entries = new List<InventoryEntry>();
        foreach (byte[] part in parts)
        {
            Assert.That(InventorySnapshot.TryRead(part, out InventorySnapshot? read), Is.True);
            entries.AddRange(read!.Entries);
        }

        server.Transport.ClearSent();
        long first = entries.First().InventoryItem;

        server.SendRemoveCard(player, smith.Id, first, 2, Bane, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(parts, Has.Count.GreaterThan(1), "in parts");
        Assert.That(parts.Max(part => part.Length), Is.LessThanOrEqualTo(ProtocolLimits.MaxReliableMessageBytes));
        Assert.That(entries.Select(entry => entry.InventoryItem).Distinct().Count(), Is.EqualTo(100), "whole");
        Assert.That(
            entries.All(entry => entry.Cards.Select(card => card.Value).SequenceEqual(new[] { Keen, Bane })),
            Is.True,
            "every row with its cards");
        Assert.That(answer, Is.EqualTo(CommandRejectionReason.InventoryFull), "no stack of Bane, no row free");
        Assert.That(server.Store.CardCommits, Is.Empty);
    }
}
}
