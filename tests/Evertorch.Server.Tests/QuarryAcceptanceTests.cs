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
///     The Milestone 21 "Ashen Quarry" exit criterion's server side end to end (ROADMAP §8): the composed server host
///     on a real PostgreSQL 18 and real UDP sockets on loopback, with three clients built from the client's production
///     networking and gameplay code. Kael and Tarn, Vanguards, and Ilsa, an Arcanist, each stored at base level 25 and
///     job level 10 with the balance baseline's build and the complete +0 dungeon set (Prototype Content §6), enter the
///     training ground together; the Smith sends its removal fees and the Gate Warden offers its quests; the three
///     cross to the training field and walk up under its north wall; and after a clean stop a second host on the same
///     database finds them there with their builds and their gear. Each later line of the milestone adds its steps.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class QuarryAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingField = "map.training_field";
    private const string GateWarden = "npc.gate_warden";
    private const string Smith = "npc.smith";
    private const string Vanguard = "job.vanguard";
    private const string Arcanist = "job.arcanist";
    private const string KaelIdentity = "quarry-kael";
    private const string KaelName = "Kael";
    private const string TarnIdentity = "quarry-tarn";
    private const string TarnName = "Tarn";
    private const string IlsaIdentity = "quarry-ilsa";
    private const string IlsaName = "Ilsa";

    private static readonly TimeSpan WalkLimit = TimeSpan.FromSeconds(30);

    // The balance baseline (owner decision 24): every point of base level 25 and job level 10 spent but the Arcanist's
    // last, which buys no raise.
    private static readonly BuildSeed VanguardBuild = new(
        10,
        new Dictionary<string, int>
        {
            ["skill.strike"] = 3,
            ["skill.first_aid"] = 1,
            ["skill.focus"] = 3,
            ["skill.heavy_blow"] = 5,
            ["skill.war_cry"] = 3,
            ["skill.iron_guard"] = 3
        },
        Vanguard);

    private static readonly BuildSeed ArcanistBuild = new(
        10,
        new Dictionary<string, int>
        {
            ["skill.arcane_bolt"] = 5,
            ["skill.clarity"] = 3,
            ["skill.mend"] = 3,
            ["skill.frost_lance"] = 5,
            ["skill.strike"] = 1,
            ["skill.focus"] = 1
        },
        Arcanist);

    private static readonly int[] VanguardStats = { 25, 8, 15, 5, 20, 5 };
    private static readonly int[] ArcanistStats = { 5, 5, 15, 30, 15, 5 };

    // The complete +0 dungeon set, one piece a position, the weapon by job.
    private static readonly (string Item, EquipmentSlot Slot)[] DungeonSet =
    {
        ("item.armor.leather", EquipmentSlot.Armor),
        ("item.headgear.grotto_circlet", EquipmentSlot.Headgear),
        ("item.shield.carapace", EquipmentSlot.Shield),
        ("item.garment.gloom_cape", EquipmentSlot.Garment),
        ("item.footwear.grotto_boots", EquipmentSlot.Footwear),
        ("item.accessory.grotto_charm", EquipmentSlot.Accessory1),
        ("item.accessory.grotto_charm", EquipmentSlot.Accessory2)
    };

    // East of the training ground's spawn point, where both the Smith and the Gate Warden are in view.
    private static readonly WorldPosition BackInTown = new(4f, 0f, 0f);

    // Under the training field's north wall, two metres in front of the cells that Milestone 21 makes a gate.
    private static readonly WorldPosition UnderTheNorthWall = new(-12f, 0f, 20.5f);

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
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=21",
                "--Session:ReconnectGraceMs=0"
            },
            contentRootPath,
            m_database.ConnectionString);
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private void EnterTogether(int port, SocketClient kael, SocketClient tarn, SocketClient ilsa)
    {
        const string step = "enter";
        kael.AfterCreate = () => Store(KaelName, VanguardBuild, VanguardStats, "item.weapon.iron_sword");
        tarn.AfterCreate = () => Store(TarnName, VanguardBuild, VanguardStats, "item.weapon.iron_sword");
        ilsa.AfterCreate = () => Store(IlsaName, ArcanistBuild, ArcanistStats, "item.weapon.ash_staff");
        SocketClient[] party = { kael, tarn, ilsa };
        foreach (SocketClient client in party)
        {
            client.Connect(port);
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => party.All(client => client.Connection.World?.Inventory.IsCurrent == true),
                party),
            Is.True,
            $"{step}: all three entered");
        foreach (SocketClient client in party)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), $"{step}: in town");
        }

        AssertBuilt(step, kael, Vanguard, VanguardBuild, VanguardStats, 0, "item.weapon.iron_sword");
        AssertBuilt(step, tarn, Vanguard, VanguardBuild, VanguardStats, 0, "item.weapon.iron_sword");
        AssertBuilt(step, ilsa, Arcanist, ArcanistBuild, ArcanistStats, 1, "item.weapon.ash_staff");
    }

    // The stored build: its job, its skills, and its statistics, every point spent but the Arcanist's last; and the
    // eight positions worn (Gameplay Systems §2, §11.1).
    private static void AssertBuilt(
        string step,
        SocketClient client,
        string job,
        BuildSeed build,
        IReadOnlyList<int> stats,
        int statPointsLeft,
        string weapon)
    {
        ClientWorld world = client.World;
        Assert.That(
            SocketClients.PumpUntil(() => world.Sheet != null && world.Skills.Count > 0, client),
            Is.True,
            $"{step}: the sheet and the skill list");
        CharacterSheet sheet = world.Sheet!;
        Assert.That(sheet.Job, Is.EqualTo(new JobDefinitionId(job)), $"{step}: the job");
        Assert.That(sheet.Stats.Select(stat => (int)stat.Value), Is.EqualTo(stats), $"{step}: the statistics");
        Assert.That(
            ((int)sheet.StatPoints, (int)sheet.SkillPoints),
            Is.EqualTo((statPointsLeft, 0)),
            $"{step}: the points left");
        Assert.That(
            world.Skills.Where(entry => entry.Level > 0).Select(entry => (entry.Skill.Value, (int)entry.Level)),
            Is.EquivalentTo(build.Skills.Select(skill => (skill.Key, skill.Value))),
            $"{step}: the stored skills");
        Assert.That(
            world.Inventory.Rows
                .Where(row => row.Slot != EquipmentSlot.None)
                .Select(row => (row.Item.Value, row.Slot))
                .OrderBy(row => row.Slot),
            Is.EqualTo(new[] { (weapon, EquipmentSlot.Weapon) }.Concat(DungeonSet).OrderBy(row => row.Item2)),
            $"{step}: the eight positions worn");
    }

    // The player walks up to an NPC, whose window opens on arrival, and returns it.
    private static EntityId WalkUpTo(string step, string npc, SocketClient client, params SocketClient[] party)
    {
        ClientWorld world = client.World;
        RemoteEntity? found = null;
        Assert.That(
            SocketClients.PumpUntil(
                () => (found = world.Remotes.Values.FirstOrDefault(remote => remote.DefinitionId == npc)) != null,
                party),
            Is.True,
            $"{step}: {npc} in view");
        int windows = client.NpcWindows.Count;
        client.TalkTo(found!.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => client.NpcWindows.Count > windows, WalkLimit, party),
            Is.True,
            $"{step}: walked up to {npc}");
        return found.Entity;
    }

    // The Smith, west of the spawn point, sends its removal fee for every card in content, and the Gate Warden, by the
    // east gate, offers its quests in order of ID (Gameplay Systems §2.2, §11.7).
    private static void VisitTheSmithAndTheGateWarden(
        ServerContent content,
        SocketClient kael,
        params SocketClient[] party)
    {
        const string step = "in town";
        ClientWorld world = kael.World;
        EntityId smith = WalkUpTo(step, Smith, kael, party);
        int cards = content.Items.Values.Count(item => item.Card != null);
        Assert.That(
            SocketClients.PumpUntil(() => world.CardRemovalFeesOf(smith).Count == cards, party),
            Is.True,
            $"{step}: the Smith's fees for every card");
        Assert.That(cards, Is.EqualTo(13), $"{step}: the cards in content");

        // Back east of the spawn point, from where the Gate Warden by the east gate is in view.
        Assert.That(kael.Controller.TryMoveTo(world.Predictor.Position, BackInTown), Is.True, $"{step}: a way back");
        Assert.That(
            SocketClients.PumpUntil(() => !kael.Controller.HasPath, WalkLimit, party),
            Is.True,
            $"{step}: back by the spawn point");
        EntityId warden = WalkUpTo(step, GateWarden, kael, party);
        Assert.That(
            world.TryGetNpcServices(warden, out NpcServices? services),
            Is.True,
            $"{step}: the Gate Warden's offers");
        Assert.That(
            services!.Offers.Select(offer => offer.Quest.Value),
            Is.EqualTo(new[] { "quest.crawler_hunt", "quest.grotto_hunt", "quest.slime_monarch" }),
            $"{step}: the Gate Warden's quests");
    }

    // The party walks into the portal before the ground's east gate, and each follows its character to the field.
    private static void CrossToTheField(ServerContent content, params SocketClient[] party)
    {
        const string step = "to the field";
        var field = new MapDefinitionId(TrainingField);
        MapPortal portal = content.Maps[new MapDefinitionId(TrainingGround)].Portals.Single();
        Assert.That(portal.DestinationMap, Is.EqualTo(field), step);
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.Controller.TryMoveTo(
                    client.World.Predictor.Position,
                    new WorldPosition(portal.Center.X - 0.3f, 0f, portal.Center.Z)),
                Is.True,
                $"{step}: a way into the portal");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => party.All(client => client.Connection.World?.Map == field
                    && client.Connection.World.Inventory.IsCurrent),
                WalkLimit,
                party),
            Is.True,
            $"{step}: every member followed its character to the field");
    }

    // The party walks up under the field's north wall; nothing there leads on yet.
    private static void WalkUnderTheNorthWall(params SocketClient[] party)
    {
        const string step = "the north wall";
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.Controller.TryMoveTo(client.World.Predictor.Position, UnderTheNorthWall),
                Is.True,
                $"{step}: a way to the wall");
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => !client.Controller.HasPath), WalkLimit, party),
            Is.True,
            $"{step}: every member arrived");
        SocketClients.PumpFor(TimeSpan.FromSeconds(1), party);
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.World.Map,
                Is.EqualTo(new MapDefinitionId(TrainingField)),
                $"{step}: still on the field");
            Assert.That(client.DistanceTo(UnderTheNorthWall), Is.LessThan(0.5f), $"{step}: under the wall");
        }
    }

    private void Store(string name, BuildSeed build, IReadOnlyList<int> stats, string weapon)
    {
        build.Apply(m_database.ConnectionString, name);
        Execute(
            $"UPDATE characters SET base_level = 25, str = {stats[0]}, agi = {stats[1]}, vit = {stats[2]}, "
            + $"int = {stats[3]}, dex = {stats[4]}, luk = {stats[5]}, hp = 9999, sp = 9999 WHERE name = '{name}'");
        StoreWorn(name, weapon, EquipmentSlot.Weapon);
        foreach ((string item, EquipmentSlot slot) in DungeonSet)
        {
            StoreWorn(name, item, slot);
        }
    }

    private void StoreWorn(string name, string item, EquipmentSlot slot)
    {
        Execute(
            "WITH worn AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"version) SELECT id, '{item}', 1, 0, 0 FROM characters WHERE name = '{name}' "
            + "RETURNING character_id, id) INSERT INTO equipment (character_id, slot, inventory_item_id, version) "
            + $"SELECT character_id, '{slot}', id, 0 FROM worn");
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        Assert.That(command.ExecuteNonQuery(), Is.EqualTo(1), sql);
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

    private static void AssertCleanTraffic(string step, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
            Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
        }
    }

    [Test]
    public void AParty_OverRealSocketsAndPostgres_GoesNorthAcrossTheFieldAndKeepsItsBuildsThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(Path.Combine(root.Path, "content", "server"), PackageFixture.BuildRepositoryPackage());

        ServerContent content;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var kael = new SocketClient(content, KaelIdentity, KaelName);
            using var tarn = new SocketClient(content, TarnIdentity, TarnName);
            using var ilsa = new SocketClient(content, IlsaIdentity, IlsaName);
            EnterTogether(port, kael, tarn, ilsa);
            AssertCleanTraffic("enter", kael, tarn, ilsa);

            VisitTheSmithAndTheGateWarden(content, kael, kael, tarn, ilsa);
            AssertCleanTraffic("in town", kael, tarn, ilsa);

            CrossToTheField(content, kael, tarn, ilsa);
            AssertCleanTraffic("to the field", kael, tarn, ilsa);

            WalkUnderTheNorthWall(kael, tarn, ilsa);
            AssertCleanTraffic("the north wall", kael, tarn, ilsa);
            Stop(host, kael, tarn, ilsa);
        }

        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var kael = new SocketClient(content, KaelIdentity, KaelName);
            using var tarn = new SocketClient(content, TarnIdentity, TarnName);
            using var ilsa = new SocketClient(content, IlsaIdentity, IlsaName);
            const string step = "restart";
            foreach (SocketClient client in new[] { kael, tarn, ilsa })
            {
                client.EnterWorld(port);
                Assert.That(
                    client.World.Map,
                    Is.EqualTo(new MapDefinitionId(TrainingField)),
                    $"{step}: on the field");
                Assert.That(client.DistanceTo(UnderTheNorthWall), Is.LessThan(0.5f), $"{step}: where it stood");
            }

            AssertBuilt(step, kael, Vanguard, VanguardBuild, VanguardStats, 0, "item.weapon.iron_sword");
            AssertBuilt(step, tarn, Vanguard, VanguardBuild, VanguardStats, 0, "item.weapon.iron_sword");
            AssertBuilt(step, ilsa, Arcanist, ArcanistBuild, ArcanistStats, 1, "item.weapon.ash_staff");
            AssertCleanTraffic(step, kael, tarn, ilsa);
            Stop(host, kael, tarn, ilsa);
        }
    }
}
}
