using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The client's real pickup code against the server's real checks and commit, over a link with latency
///     (Gameplay Systems §11, §12 steps 7–9).
/// </summary>
[TestFixture]
public sealed class ClientPickupTests
{
    [Test]
    public void KillTheSlimeAndPickUpItsGel_EndsWithTheGelInTheCommittedInventory()
    {
        // The slime's one drop entry rolls 0, below any chance, and draws the smallest amount.
        var rig = new ClientServerRig(true, new SureHitRandom(), new ScriptedRandom(0));
        SimulatedClient client = rig.AddClient(7, 3, 17);
        client.Link.LatencyMilliseconds = 50;
        rig.ConnectAll();
        MapInstance map = rig.Server.World.Maps.Single();
        MonsterEntity slime = rig.Server.MonstersNear(map.Definition.SpawnPosition).First();
        rig.AdvanceUntil(() => client.Connection.World != null && client.World.Remotes.ContainsKey(slime.Id), 3000);
        var dropped = new List<ItemDropped>();
        var rejections = new List<CommandRejected>();
        client.World.ItemDroppedReceived += dropped.Add;
        client.World.CommandRejectedReceived += rejections.Add;
        client.AutoAttack!.Attack(slime.Id);
        Assert.That(rig.AdvanceUntil(() => dropped.Count > 0, 30000), Is.GreaterThan(0), "the slime dropped gel");
        EntityId gel = dropped.Single().Entity;
        rig.AdvanceUntil(() => !client.AutoAttack.IsActive, 3000);

        client.Pickup!.Pickup(gel);
        int took = rig.AdvanceUntil(() => client.World.Inventory.Rows.Count > 0, 10000);

        Assert.That(took, Is.GreaterThan(0), "the gel reached the client's inventory");
        InventoryEntry row = client.World.Inventory.Rows.Single();
        Assert.That(row.Item, Is.EqualTo(new ItemDefinitionId("item.material.slime_gel")));
        Assert.That(row.Quantity, Is.EqualTo(1u));
        Assert.That(client.World.Inventory.Revision, Is.EqualTo(1u));
        Assert.That(client.World.Remotes.ContainsKey(gel), Is.False);
        Assert.That(client.Pickup.IsActive, Is.False);
        Assert.That(client.Pickup.PickupsSent, Is.EqualTo(1));
        Assert.That(rejections, Is.Empty);
        Assert.That(map.ItemDrops, Is.Empty);
        Assert.That(rig.Server.Store.Stored(7).Items.Single().Quantity, Is.EqualTo(1));
        Assert.That(rig.Server.Store.LedgerCount, Is.EqualTo(1));
    }

    [Test]
    public void Pickup_OfADropAcrossTheYard_WalksUpAndIsAcceptedFirstTime()
    {
        var rig = new ClientServerRig();
        SimulatedClient client = rig.AddClient(7, 1, 17);
        client.Link.LatencyMilliseconds = 100;
        rig.ConnectAll();
        rig.AdvanceUntil(() => client.Connection.World?.Inventory.IsCurrent == true, 3000);
        MapInstance map = rig.Server.World.Maps.Single();
        WorldPosition spawn = map.Definition.SpawnPosition;
        ItemDropEntity drop = rig.Server.World.SpawnItemDrop(
            map,
            new ItemDefinitionId("item.material.slime_gel"),
            2,
            new WorldPosition(spawn.X + 4f, spawn.Y, spawn.Z),
            rig.Server.CurrentTick,
            long.MaxValue,
            default);
        rig.AdvanceUntil(() => client.World.Remotes.ContainsKey(drop.Id), 3000);

        client.Pickup!.Pickup(drop.Id);
        int took = rig.AdvanceUntil(() => client.World.Inventory.Rows.Count > 0, 10000);

        Assert.That(took, Is.GreaterThan(0));
        Assert.That(client.World.Inventory.Rows.Single().Quantity, Is.EqualTo(2u));
        Assert.That(rig.Server.SessionOf(rig.Server.Sessions.Sessions.Single().Connection).RefusedCommands, Is.Zero);
        Assert.That(client.World.Smoother.Snaps, Is.Zero);
    }
}
}
