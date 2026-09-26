using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     How often the server asks the ledger about an inventory commit whose answer was lost (Persistence §9): as soon
///     as the database takes work again after an outage, but a second after a lookup that failed for another reason,
///     not on every tick with an error each time.
/// </summary>
[TestFixture]
public sealed class LedgerLookupPacingTests
{
    private const string SlimeGel = "item.material.slime_gel";
    private const string Health = "item.consumable.minor_health";

    // A pickup of a drop at the player's feet whose commit happens and loses its answer, so the ledger must settle it.
    private static (TestServer Server, ItemDropEntity Drop) PickUpWithTheAnswerLost()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            2,
            server.PlayerOf(picker).Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.Store.AmbiguousPickupFailures = 100;
        server.SendPickup(picker, drop.Id, 1);
        return (server, drop);
    }

    // The ticks between one lookup of the store and the next, from the start, until the operation settles.
    private static List<int> LookupGaps(TestServer server, Func<bool> isSettled)
    {
        var gaps = new List<int>();
        int seen = server.Store.Lookups.Count;
        int ticks = 0;
        for (int tick = 0; tick < 5 * TestServer.TickRate && !isSettled(); tick++)
        {
            server.Tick();
            ticks++;
            if (server.Store.Lookups.Count > seen)
            {
                gaps.Add(ticks);
                seen = server.Store.Lookups.Count;
                ticks = 0;
            }
        }

        return gaps;
    }

    private static int FailedJobs(TestServer server)
    {
        return server.PersistenceLog.Entries.Count(entry => entry.EventId.Name == "PersistenceJobFailed");
    }

    [Test]
    public void ItemLookup_ThatFails_IsAskedAgainASecondLater()
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, Health, 1, 3, 5);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        long potions = server.Store.Stored(1).Items.Single().Id;
        server.Store.AmbiguousConsumeFailures = 100;
        server.Store.FailingLookups = 2;
        server.SendUseItem(player, potions, 1);
        server.Tick();

        List<int> gaps = LookupGaps(server, () => server.SessionOf(player).Character!.Operation == null);

        Assert.That(gaps, Has.Count.EqualTo(3), "two failures, then the answer");
        Assert.That(gaps.Skip(1), Is.All.InRange(TestServer.TickRate, TestServer.TickRate + 1));
        Assert.That(FailedJobs(server), Is.EqualTo(2), "one error for each failed lookup");
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(2), "one unit spent once");
    }

    [Test]
    public void PickupLookup_ThatFails_IsAskedAgainASecondLater()
    {
        (TestServer server, ItemDropEntity drop) = PickUpWithTheAnswerLost();
        server.Store.FailingLookups = 2;

        List<int> gaps = LookupGaps(server, () => !server.World.Maps.Single().Contains(drop.Id));

        Assert.That(gaps, Has.Count.EqualTo(3), "two failures, then the answer");
        Assert.That(gaps.Skip(1), Is.All.InRange(TestServer.TickRate, TestServer.TickRate + 1));
        Assert.That(FailedJobs(server), Is.EqualTo(2), "one error for each failed lookup");
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False, "picked up once the ledger answered");
    }

    // The outage is shorter than a second, so a lookup held back as a failed one would still wait when it ends.
    [Test]
    public void PickupLookup_ThatMeetsAnOutage_IsAskedAgainAsSoonAsTheDatabaseTakesWork()
    {
        (TestServer server, ItemDropEntity drop) = PickUpWithTheAnswerLost();
        server.Tick(2);
        server.Store.IsUnavailable = true;
        server.Tick(5);
        int lookupsInTheOutage = server.Store.Lookups.Count;
        server.Store.IsUnavailable = false;

        List<int> gaps = LookupGaps(server, () => !server.World.Maps.Single().Contains(drop.Id));

        Assert.That(lookupsInTheOutage, Is.Positive, "the lookup met the outage");
        Assert.That(gaps, Is.EqualTo(new[] { 2 }), "the probe's tick, then the lookup's");
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
    }
}
}
