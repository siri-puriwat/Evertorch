using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A pickup is in PostgreSQL before any client hears of it, and it outlives the server process (Gameplay Systems
///     §12 steps 8–10; Persistence §5, §11). The servers share one real PostgreSQL 18 database.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PickupDurabilityTests
{
    private const string SlimeGel = "item.material.slime_gel";

    private PostgresFixture m_database = null!;
    private PostgresGameStore m_store = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_store = new PostgresGameStore(m_database.ConnectionString);
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private TestServer NewServer()
    {
        return new TestServer(store: new PostgresGameStore(m_database.ConnectionString));
    }

    private static ItemDropEntity DropNextTo(TestServer server, ConnectionId connection, uint amount)
    {
        PlayerEntity player = server.PlayerOf(connection);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            amount,
            new WorldPosition(player.Position.X + 1f, player.Position.Y, player.Position.Z),
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        return drop;
    }

    private static bool HasBeenTold(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlOpcodesSentTo(connection)
            .Any(opcode => opcode == MessageOpcode.ItemPickedUp || opcode == MessageOpcode.InventoryChanged);
    }

    private PickupResult? Ledger(ItemDropEntity drop, long character)
    {
        return m_store.FindPickupAsync(drop.DropId, character, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Test]
    public void Pickup_IsInTheLedgerBeforeAnyClientIsTold()
    {
        TestServer server = NewServer();
        ConnectionId picker = server.EnterWorldAs("durable-order", "DurableOrder");
        long character = server.SessionOf(picker).Character!.Character.Value;
        ItemDropEntity drop = DropNextTo(server, picker, 2);
        server.Transport.ClearSent();
        server.RunsPersistence = false;
        server.SendPickup(picker, drop.Id, 1);
        server.Tick(10);

        Assert.That(HasBeenTold(server, picker), Is.False, "nothing is said while the commit is held");
        Assert.That(Ledger(drop, character), Is.Null);

        server.RunsPersistence = true;
        for (int tick = 0; tick < 10 && !HasBeenTold(server, picker); tick++)
        {
            server.Tick();
            if (HasBeenTold(server, picker))
            {
                Assert.That(Ledger(drop, character)!.Status, Is.EqualTo(PickupStatus.Committed));
            }
        }

        Assert.That(HasBeenTold(server, picker), Is.True);
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
    }

    [Test]
    public void Pickup_SurvivesTheLossOfTheServerProcess()
    {
        TestServer lost = NewServer();
        ConnectionId picker = lost.EnterWorldAs("durable-loss", "DurableLoss");
        ItemDropEntity drop = DropNextTo(lost, picker, 2);
        lost.SendPickup(picker, drop.Id, 1);
        lost.TickUntil(() => lost.Transport.ControlOpcodesSentTo(picker).Contains(MessageOpcode.InventoryChanged));

        TestServer restarted = NewServer();
        ConnectionId again = restarted.EnterWorldAs("durable-loss", "DurableLoss");

        InventorySnapshot snapshot = restarted.Transport.ControlSentTo(again)
            .Where(message => message.Opcode == MessageOpcode.InventorySnapshot)
            .Select(message =>
            {
                InventorySnapshot.TryRead(message.Payload, out InventorySnapshot? part);
                return part!;
            })
            .Single();
        Assert.That(snapshot.Revision, Is.EqualTo(1u));
        InventoryEntry row = snapshot.Entries.Single();
        Assert.That(row.Item, Is.EqualTo(new ItemDefinitionId(SlimeGel)));
        Assert.That(row.Quantity, Is.EqualTo(2u));
    }
}
}
