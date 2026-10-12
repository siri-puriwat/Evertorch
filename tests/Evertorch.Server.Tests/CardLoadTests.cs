using System.Linq;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A character's rows load with their cards (Persistence §8): a stored card the content lacks refuses the entry, as
///     an unknown item does, and keeps the data.
/// </summary>
[TestFixture]
public sealed class CardLoadTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string SlimeGel = "item.material.slime_gel";

    private static long RowOf(TestServer server, long character, string item)
    {
        return server.Store.Stored(character).Items.Single(stored => stored.ItemDefinitionId == item).Id;
    }

    private static SessionState Enter(TestServer server, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, character);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State != SessionState.EnteringWorld);
        return server.SessionOf(connection).State;
    }

    [Test]
    public void Load_OfARowHoldingACardTheContentKnows_EntersTheWorld()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.GiveItems(7, Sword, 1, 1, 1);
        server.Store.Card(7, RowOf(server, 7, Sword), SlimeGel);

        Assert.That(Enter(server, 7), Is.EqualTo(SessionState.InWorld));
    }

    [Test]
    public void Load_OfARowHoldingACardTheContentLacks_IsRefused_AndTheDataIsKept()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.GiveItems(7, Sword, 1, 1, 1);
        long sword = RowOf(server, 7, Sword);
        server.Store.Card(7, sword, "item.card.retired");

        Assert.That(Enter(server, 7), Is.EqualTo(SessionState.Authenticated));
        Assert.That(
            server.Log.Entries
                .Where(entry => entry.EventId.Name == "CharacterContentMismatch")
                .Select(entry => entry.Message),
            Has.Some.Contains("card 'item.card.retired'"));
        Assert.That(server.Store.Stored(7).Items.Single(item => item.Id == sword).Cards,
            Is.EqualTo(new[] { "item.card.retired" }));
    }
}
}
