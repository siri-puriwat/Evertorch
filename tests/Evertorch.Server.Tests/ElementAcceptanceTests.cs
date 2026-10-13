using System;
using System.Collections.Generic;
using System.Globalization;
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
///     The Milestone 18 "Elements, races, and sizes" exit criterion's server side end to end (ROADMAP §8): the composed
///     server host on a real PostgreSQL 18 and real UDP sockets on loopback, with scripted combat and drops, and two
///     clients of two accounts built from the client's production networking and gameplay code. Vera, stored on the
///     training field wearing a training sword and holding cloth armor and a Grotto Crawler Card, and Arlo, of another
///     account, stored beside her as an Arcanist wearing the training staff with Arcane Bolt learned, enter and see each
///     other by name. Vera's sword fells a Spark Wisp, every hit for the sword's least draw, and Arlo's bolt strikes
///     another. Then Vera leaves, and Arlo sees her go. After a clean stop a second host on the same database lets both
///     in again on the field, their bags as they left them.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ElementAcceptanceTests
{
    private const string TrainingField = "map.training_field";
    private const string SparkWisp = "monster.spark_wisp";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string TrainingStaff = "item.weapon.training_staff";
    private const string ClothArmor = "item.armor.cloth";
    private const string GrottoCrawlerCard = "item.card.grotto_crawler";
    private const string Arcanist = "job.arcanist";
    private const string VeraIdentity = "element-vera";
    private const string VeraName = "Vera";
    private const string ArloIdentity = "element-arlo";
    private const string ArloName = "Arlo";

    // The training sword's least draw, 19, and twice the status attack of 7, less the wisp's 1 defense
    // (Gameplay Systems §11.1).
    private const uint SwordOnAWisp = 32;

    private static readonly SkillDefinitionId ArcaneBolt = new("skill.arcane_bolt");
    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);

    // West of the field's three Spark Wisps, beyond what the aggressive crawlers in its north notice.
    private static readonly WorldPosition VeraStands = new(0.5f, 0f, -10.5f);
    private static readonly WorldPosition ArloStands = new(0.5f, 0f, -8.5f);

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

    // No reconnect grace, so a player who leaves is out of the world at once. Every hit lands at the least of its
    // draws, and nothing drops, since network timing decides which draw each roll gets (Coding Standards §10).
    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=18",
                "--Session:ReconnectGraceMs=0"
            },
            contentRootPath,
            m_database.ConnectionString);
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(999_999));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private void Enter(int port, SocketClient vera, SocketClient arlo)
    {
        const string step = "enter";
        vera.AfterCreate = () =>
        {
            Place(VeraName, VeraStands);
            StoreWorn(VeraName, TrainingSword);
            StoreItem(VeraName, ClothArmor);
            StoreItem(VeraName, GrottoCrawlerCard);
        };
        arlo.AfterCreate = () =>
        {
            Place(ArloName, ArloStands);
            new BuildSeed(1, new Dictionary<string, int> { [ArcaneBolt.Value] = 1 }, Arcanist)
                .Apply(m_database.ConnectionString, ArloName);
            StoreWorn(ArloName, TrainingStaff);
        };
        vera.EnterWorld(port);
        arlo.Connect(port);
        SocketClient[] both = { vera, arlo };
        bool isEntered = SocketClients.PumpUntil(
            () => both.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            both);
        string states = string.Join(", ",
            both.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"{step}: both entered ({states})");
        foreach (SocketClient client in both)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingField)), $"{step}: on the field");
        }

        Assert.That(
            Bag(vera),
            Is.EqualTo(new[] { $"{ClothArmor} x 1", $"{GrottoCrawlerCard} x 1", $"{TrainingSword} x 1 worn" }),
            $"{step}: Vera's bag as stored");
        Assert.That(Bag(arlo), Is.EqualTo(new[] { $"{TrainingStaff} x 1 worn" }), $"{step}: Arlo's bag as stored");
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

    // Tab until the server confirms a target of the given monster.
    private static EntityId TargetA(string step, string monster, SocketClient client, SocketClient other)
    {
        ClientWorld world = client.World;
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Remotes.Values.Any(remote => remote.DefinitionId == monster && !remote.IsDead),
                client,
                other),
            Is.True,
            $"{step}: a {monster} in view");
        for (int tries = 0; tries < 8 && !IsLive(world, world.Target, monster); tries++)
        {
            EntityId next = client.CycleTarget(true);
            SocketClients.PumpUntil(() => world.Target == next, client, other);
        }

        Assert.That(IsLive(world, world.Target, monster), Is.True, $"{step}: Tab found a {monster}");
        return world.Target;
    }

    private static bool IsLive(ClientWorld world, EntityId entity, string monster)
    {
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote)
            && remote.DefinitionId == monster
            && !remote.IsDead;
    }

    // West attacks the wisp Tab found: the client walks into range, and every scripted hit lands for the same amount
    // until the wisp falls (Gameplay Systems §6, §11.1).
    private static void FellAWisp(SocketClient vera, SocketClient arlo)
    {
        const string step = "the sword";
        ClientWorld world = vera.World;
        EntityId wisp = TargetA(step, SparkWisp, vera, arlo);
        var damages = new List<Damage>();
        var deaths = new List<EntityDied>();
        world.DamageReceived += damages.Add;
        world.EntityDiedReceived += deaths.Add;

        vera.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(() => deaths.Any(death => death.Entity == wisp), FightLimit, vera, arlo),
            Is.True,
            $"{step}: the wisp fell");
        world.DamageReceived -= damages.Add;
        world.EntityDiedReceived -= deaths.Add;

        uint[] hits = damages
            .Where(damage => damage.Source == world.LocalEntity && damage.Target == wisp)
            .Select(damage => damage.Amount)
            .ToArray();
        Assert.That(hits, Has.Length.EqualTo(2).And.All.EqualTo(SwordOnAWisp), $"{step}: two hits of the sword");
        Assert.That(world.IsLocalDead, Is.False, $"{step}: Vera survived");
        Assert.That(
            SocketClients.PumpUntil(() => !vera.AutoAttack.IsActive, vera, arlo),
            Is.True,
            $"{step}: the attack ended");
    }

    // Arlo targets another wisp and casts Arcane Bolt: the client walks into the bolt's range, and the bolt strikes
    // (Gameplay Systems §9).
    private static void BoltAWisp(SocketClient arlo, SocketClient vera)
    {
        const string step = "the bolt";
        ClientWorld world = arlo.World;
        EntityId wisp = TargetA(step, SparkWisp, arlo, vera);
        var resolved = new List<SkillResolved>();
        world.SkillResolvedReceived += resolved.Add;

        Assert.That(arlo.UseSkill(ArcaneBolt), Is.True, $"{step}: cast");
        Assert.That(
            SocketClients.PumpUntil(
                () => resolved.Any(skill => skill.Caster == world.LocalEntity && skill.Skill == ArcaneBolt),
                FightLimit,
                arlo,
                vera),
            Is.True,
            $"{step}: resolved ({world.LastRejection})");
        world.SkillResolvedReceived -= resolved.Add;

        SkillResolved bolt = resolved.First(skill => skill.Caster == world.LocalEntity && skill.Skill == ArcaneBolt);
        Assert.That((bolt.Target, bolt.Outcome), Is.EqualTo((wisp, SkillOutcome.Hit)), $"{step}: on the wisp");
        Assert.That(bolt.Amount, Is.GreaterThan(0u), $"{step}: some damage");
    }

    // Vera leaves; with no grace her character leaves the world at once, and Arlo sees her go.
    private static void Leave(SocketClient vera, SocketClient arlo)
    {
        const string step = "away";
        EntityId gone = vera.World.LocalEntity;
        vera.Disconnect();
        Assert.That(
            SocketClients.PumpUntil(() => !arlo.World.Remotes.ContainsKey(gone), arlo),
            Is.True,
            $"{step}: Arlo saw Vera go");
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

    private static string[] Bag(SocketClient client)
    {
        return client.World.Inventory.Rows
            .Select(row => $"{row.Item.Value} x {row.Quantity}"
                + (row.Cards.Count == 0
                    ? string.Empty
                    : $" [{string.Join(", ", row.Cards.Select(card => card.Value))}]")
                + (row.Slot == EquipmentSlot.None ? string.Empty : " worn"))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToArray();
    }

    private void Place(string name, WorldPosition position)
    {
        Execute(
            $"UPDATE characters SET map_definition_id = '{TrainingField}', position_x = {Number(position.X)}, "
            + $"position_y = {Number(position.Y)}, position_z = {Number(position.Z)} WHERE name = '{name}'");
    }

    private static string Number(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void StoreItem(string name, string item)
    {
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT id, '{item}', 1, 0, 0 FROM characters WHERE name = '{name}'");
    }

    private void StoreWorn(string name, string weapon)
    {
        Execute(
            "WITH worn AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"version) SELECT id, '{weapon}', 1, 0, 0 FROM characters WHERE name = '{name}' "
            + "RETURNING character_id, id) INSERT INTO equipment (character_id, slot, inventory_item_id, version) "
            + "SELECT character_id, 'Weapon', id, 0 FROM worn");
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
    public void TwoPlayers_TwoAccounts_FightOnTheField_AndKeepTheirBagsThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        ServerContent content;
        string[] veraBag;
        string[] arloBag;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var vera = new SocketClient(content, VeraIdentity, VeraName);
            using var arlo = new SocketClient(content, ArloIdentity, ArloName);
            Enter(port, vera, arlo);
            AssertSees("enter", vera, arlo, ArloName);
            AssertSees("enter", arlo, vera, VeraName);
            AssertCleanTraffic("enter", vera, arlo);

            FellAWisp(vera, arlo);
            AssertCleanTraffic("the sword", vera, arlo);
            BoltAWisp(arlo, vera);
            AssertCleanTraffic("the bolt", vera, arlo);
            veraBag = Bag(vera);
            arloBag = Bag(arlo);
            Leave(vera, arlo);
            AssertCleanTraffic("away", vera, arlo);
            Stop(host, arlo);
        }

        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var vera = new SocketClient(content, VeraIdentity, VeraName);
            using var arlo = new SocketClient(content, ArloIdentity, ArloName);
            vera.EnterWorld(port);
            arlo.EnterWorld(port);
            Assert.That(Bag(vera), Is.EqualTo(veraBag), "restart: Vera's bag as it was");
            Assert.That(Bag(arlo), Is.EqualTo(arloBag), "restart: Arlo's bag as it was");
            foreach (SocketClient client in new[] { vera, arlo })
            {
                Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingField)), "restart: on the field");
            }

            AssertCleanTraffic("restart", vera, arlo);
            Stop(host, vera, arlo);
        }
    }
}
}
