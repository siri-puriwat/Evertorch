using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 20 "Equipment and visible clothing" exit criterion's server side end to end (ROADMAP §8): the
///     composed server host on a real PostgreSQL 18 and real UDP sockets on loopback, with two clients of two accounts
///     built from the client's production networking and gameplay code. Anna, stored holding a training sword, cloth
///     armor carrying a Training Slime Card, and +4 leather armor, enters the training ground beside Bobby; each sees
///     the other by name. She wears the sword and the carded cloth, which her sheet counts once each and Bobby sees in
///     her hand and inspects, swaps the cloth for the leather in one equip, and takes the sword off, which Bobby sees
///     go. Then she leaves, and after a clean stop a second host on the same database finds her bag and what she wears
///     as she left them. Each later line of the milestone adds its steps.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class EquipmentAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string ClothArmor = "item.armor.cloth";
    private const string LeatherArmor = "item.armor.leather";
    private const string Hardy = "item.card.training_slime";
    private const string AnnaIdentity = "equip-anna";
    private const string AnnaName = "Anna";
    private const string BobbyIdentity = "equip-bobby";
    private const string BobbyName = "Bobby";

    private PostgresFixture m_database = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    // No reconnect grace, so a player who leaves is out of the world at once.
    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=17",
                "--Session:ReconnectGraceMs=0"
            },
            contentRootPath,
            m_database.ConnectionString);
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0), new ScriptedRandom(99));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private void Enter(int port, SocketClient anna, SocketClient bobby)
    {
        const string step = "enter";
        anna.AfterCreate = () =>
        {
            StoreItem(AnnaName, TrainingSword, 0);
            Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
                + $"instance_data_json, version) SELECT id, '{ClothArmor}', 1, 0, "
                + $"'{{\"cards\": [\"{Hardy}\"]}}'::jsonb, 0 FROM characters WHERE name = '{AnnaName}'");
            StoreItem(AnnaName, LeatherArmor, 4);
        };
        anna.EnterWorld(port);
        bobby.Connect(port);
        SocketClient[] both = { anna, bobby };
        bool isEntered = SocketClients.PumpUntil(
            () => both.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            both);
        string states = string.Join(", ",
            both.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"{step}: both entered ({states})");
        foreach (SocketClient client in both)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), $"{step}: in town");
        }

        Assert.That(
            Bag(anna),
            Is.EqualTo(
                new[]
                {
                    $"{ClothArmor} +0 [{Hardy}]", $"{LeatherArmor} +4", $"{TrainingSword} +0"
                }),
            $"{step}: Anna's bag as stored, nothing worn");
        Assert.That(bobby.World.Inventory.Rows, Is.Empty, $"{step}: Bobby's bag");
    }

    // The viewer has a spawn for the seen character, which names it (Network Protocol §6).
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen, string name)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen),
            Is.True,
            $"{step}: {name} in view");
        Assert.That(viewer.World.Remotes[entity].Name, Is.EqualTo(name), $"{step}: by its name");
    }

    // Anna wears the sword and the carded cloth: her sheet counts the cloth's defense and its card's VIT once each, and
    // Bobby sees the sword in her hand (Gameplay Systems §2, §11.1).
    private static void Wear(SocketClient anna, SocketClient bobby)
    {
        const string step = "wear";
        WearRow(step, anna, bobby, TrainingSword, EquipmentSlot.Weapon);
        WearRow(step, anna, bobby, ClothArmor, EquipmentSlot.Armor);

        Assert.That(
            SocketClients.PumpUntil(() => anna.World.Sheet?.EquipmentDefense == 8, anna, bobby),
            Is.True,
            $"{step}: the cloth's defense counted once ({anna.World.Sheet?.EquipmentDefense})");
        Assert.That(anna.World.Sheet!.EquipmentVit, Is.EqualTo(1), $"{step}: the card's VIT counted once");
        Assert.That(
            SocketClients.PumpUntil(
                () => bobby.World.Remotes[anna.World.LocalEntity].WornWeapon == TrainingSword,
                anna,
                bobby),
            Is.True,
            $"{step}: Bobby sees the sword in Anna's hand");
    }

    // Bobby inspects Anna: the sword, and the cloth with its card; Anna hears nothing of it (Gameplay Systems §11.1).
    private static void Inspect(SocketClient anna, SocketClient bobby, string armor, int armorLevel,
        params string[] armorCards)
    {
        const string step = "inspect";
        var seen = new List<InspectedEquipment>();
        var heard = new List<InspectedEquipment>();
        bobby.Connection.InspectedEquipmentReceived += seen.Add;
        anna.Connection.InspectedEquipmentReceived += heard.Add;

        Assert.That(bobby.Connection.SendInspectPlayer(anna.World.LocalEntity), Is.Not.Zero, $"{step}: asked");
        Assert.That(SocketClients.PumpUntil(() => seen.Count == 1, anna, bobby), Is.True, $"{step}: answered");
        bobby.Connection.InspectedEquipmentReceived -= seen.Add;
        anna.Connection.InspectedEquipmentReceived -= heard.Add;

        InspectedEquipment answer = seen.Single();
        InspectedItem weapon = answer.ItemIn(EquipmentSlot.Weapon);
        InspectedItem worn = answer.ItemIn(EquipmentSlot.Armor);
        Assert.That(
            (answer.Entity, weapon.Item.Value, weapon.RefineLevel, weapon.Cards.Count),
            Is.EqualTo((anna.World.LocalEntity, TrainingSword, (byte)0, 0)),
            $"{step}: the sword");
        Assert.That((worn.Item.Value, worn.RefineLevel), Is.EqualTo((armor, (byte)armorLevel)), $"{step}: the armor");
        Assert.That(worn.Cards.Select(card => card.Value), Is.EqualTo(armorCards), $"{step}: its cards");
        Assert.That(answer.Entries.Skip(2).Select(entry => entry.Item.IsEmpty), Is.All.True, $"{step}: the rest empty");
        Assert.That(heard, Is.Empty, $"{step}: Anna hears nothing");
    }

    // One equip wears the +4 leather in the cloth's place: the cloth comes back to the bag in the same commit, and the
    // sheet counts the leather and its refine bonus alone (Gameplay Systems §11.1, §11.6).
    private static void Swap(SocketClient anna, SocketClient bobby)
    {
        const string step = "swap";
        long cloth = Row(anna, ClothArmor).InventoryItem;
        WearRow(step, anna, bobby, LeatherArmor, EquipmentSlot.Armor);
        Assert.That(
            anna.World.Inventory.Rows.Single(row => row.InventoryItem == cloth).Slot,
            Is.EqualTo(EquipmentSlot.None),
            $"{step}: the cloth back in the bag");
        Assert.That(
            SocketClients.PumpUntil(() => anna.World.Sheet?.EquipmentDefense == 35, anna, bobby),
            Is.True,
            $"{step}: the leather's 15 and its +4's 20 ({anna.World.Sheet?.EquipmentDefense})");
        Assert.That(anna.World.Sheet!.EquipmentVit, Is.Zero, $"{step}: the cloth's card no longer counts");
    }

    // Anna takes the sword off, and Bobby sees her hand empty (Gameplay Systems §11.1).
    private static void TakeOffTheSword(SocketClient anna, SocketClient bobby)
    {
        const string step = "unequip";
        long sword = Row(anna, TrainingSword).InventoryItem;
        Assert.That(anna.Connection.SendUnequip(EquipmentSlot.Weapon), Is.Not.Zero, $"{step}: sent");
        Assert.That(
            SocketClients.PumpUntil(
                () => anna.World.Inventory.Rows.Any(row =>
                    row.InventoryItem == sword && row.Slot == EquipmentSlot.None),
                anna,
                bobby),
            Is.True,
            $"{step}: the sword in the bag ({anna.World.LastRejection})");
        Assert.That(
            SocketClients.PumpUntil(
                () => bobby.World.Remotes[anna.World.LocalEntity].WornWeapon.Length == 0,
                anna,
                bobby),
            Is.True,
            $"{step}: Bobby sees Anna's hand empty");
    }

    private static void WearRow(string step, SocketClient anna, SocketClient bobby, string item, EquipmentSlot slot)
    {
        long row = Row(anna, item).InventoryItem;
        Assert.That(anna.Connection.SendEquip(row, slot), Is.Not.Zero, $"{step}: {item} sent");
        Assert.That(
            SocketClients.PumpUntil(
                () => anna.World.Inventory.Rows.Any(entry => entry.InventoryItem == row && entry.Slot == slot),
                anna,
                bobby),
            Is.True,
            $"{step}: {item} worn ({anna.World.LastRejection})");
    }

    private static InventoryEntry Row(SocketClient client, string item)
    {
        return client.World.Inventory.Rows.Single(row => row.Item.Value == item);
    }

    // Anna leaves; with no grace her character leaves the world at once, and Bobby sees her go.
    private static void Leave(SocketClient anna, SocketClient bobby)
    {
        const string step = "away";
        EntityId gone = anna.World.LocalEntity;
        anna.Disconnect();
        Assert.That(
            SocketClients.PumpUntil(() => !bobby.World.Remotes.ContainsKey(gone), bobby),
            Is.True,
            $"{step}: Bobby saw Anna go");
    }

    private static void Stop(IHost host, params SocketClient[] clients)
    {
        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            SocketClients.PumpUntil(
                () => clients.All(client => client.Connection.State == ClientConnectionState.Disconnected),
                TimeSpan.FromSeconds(10),
                clients),
            Is.True,
            "stop: every client told");
    }

    // Each row as "<item> +<level>", its cards in brackets and its slot after "in".
    private static string[] Bag(SocketClient client)
    {
        return client.World.Inventory.Rows
            .Select(row => $"{row.Item.Value} +{row.RefineLevel}"
                + (row.Cards.Count == 0
                    ? string.Empty
                    : $" [{string.Join(", ", row.Cards.Select(card => card.Value))}]")
                + (row.Slot == EquipmentSlot.None ? string.Empty : $" in {row.Slot}"))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToArray();
    }

    private void StoreItem(string name, string item, int refineLevel)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var insert = new NpgsqlCommand(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + "SELECT id, @item, 1, @level, 0 FROM characters WHERE name = @name",
            connection);
        insert.Parameters.AddWithValue("item", item);
        insert.Parameters.AddWithValue("level", refineLevel);
        insert.Parameters.AddWithValue("name", name);
        Assert.That(insert.ExecuteNonQuery(), Is.EqualTo(1), $"{item} stored");
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        Assert.That(command.ExecuteNonQuery(), Is.EqualTo(1), sql);
    }

    private static void AssertCleanTraffic(string step, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
            Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
        }
    }

    [Test]
    public void TwoPlayers_TwoAccounts_WearSwapAndInspect_AndKeepTheirGearThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        ServerContent content;
        string[] worn;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            Enter(port, anna, bobby);

            AssertSees("enter", anna, bobby, BobbyName);
            AssertSees("enter", bobby, anna, AnnaName);
            AssertCleanTraffic("enter", anna, bobby);

            Wear(anna, bobby);
            AssertCleanTraffic("wear", anna, bobby);
            Inspect(anna, bobby, ClothArmor, 0, Hardy);
            AssertCleanTraffic("inspect", anna, bobby);
            Swap(anna, bobby);
            Inspect(anna, bobby, LeatherArmor, 4);
            AssertCleanTraffic("swap", anna, bobby);
            TakeOffTheSword(anna, bobby);
            AssertCleanTraffic("unequip", anna, bobby);
            worn = Bag(anna);
            Leave(anna, bobby);
            AssertCleanTraffic("away", anna, bobby);
            Stop(host, bobby);
        }

        Assert.That(
            worn,
            Is.EqualTo(
                new[]
                {
                    $"{ClothArmor} +0 [{Hardy}]", $"{LeatherArmor} +4 in {EquipmentSlot.Armor}", $"{TrainingSword} +0"
                }),
            "away: the leather worn, the sword and the carded cloth in the bag");
        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            anna.EnterWorld(port);
            bobby.EnterWorld(port);
            Assert.That(Bag(anna), Is.EqualTo(worn), "restart: Anna's bag and gear as she left them");
            Assert.That(
                SocketClients.PumpUntil(() => anna.World.Sheet?.EquipmentDefense == 35, anna, bobby),
                Is.True,
                "restart: the leather counted from the first sheet");
            AssertCleanTraffic("restart", anna, bobby);
            Stop(host, anna, bobby);
        }
    }
}
}
