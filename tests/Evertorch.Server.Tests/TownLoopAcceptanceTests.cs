using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 7 town loop (ROADMAP §8) end to end: the composed server host with every map loaded on a real
///     PostgreSQL 18, real UDP sockets on loopback, and a client built from the client's production networking and
///     gameplay code, pumped on the test's thread. A player leaves the training ground, hunts forest crawlers on the
///     training field, returns, and keeps the results through a restart. Each later line of Milestone 7 adds its steps
///     here.
/// </summary>
/// <remarks>
///     Combat and drops draw from scripted sources (<see cref="TestHosts.ScriptOutcomes" />): every attack hits
///     without a critical, and every entry of a drop table drops its smallest amount. Monster placement and AI keep the
///     seeded server source.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class TownLoopAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingField = "map.training_field";
    private const string Adventurer = "job.adventurer";
    private const string ForestCrawler = "monster.forest_crawler";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string ClothArmor = "item.armor.cloth";
    private const string MinorHealth = "item.consumable.minor_health";
    private const string Quartermaster = "npc.quartermaster";
    private const string GateWarden = "npc.gate_warden";
    private const string CrawlerHunt = "quest.crawler_hunt";
    private const string Identity = "townloop";
    private const string CharacterName = "TownLoop1";
    private const int HuntedCrawlers = 5;
    private const float ConvergedDistance = 1e-3f;

    // Farther from the crawlers' home than a crawler at home perceives, max(radius, roam) + perception = 11 m, and
    // within their leash of 14 m, so a crawler led there keeps fighting and no other joins it.
    private const float StagingDistance = 12.5f;

    // Within a crawler's perception of 6 m.
    private const float LureDistance = 4.5f;

    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(40);

    // Long enough for a crawler to respawn (12 s) and for the player to walk to it.
    private static readonly TimeSpan LureLimit = TimeSpan.FromSeconds(45);

    // An attack counts as current for this long; a crawler that stopped swinging went home.
    private static readonly TimeSpan AttackMemory = TimeSpan.FromSeconds(3);

    // Where the player stops in town, away from the spawn point, so a restart that forgot the place would show.
    private static readonly WorldPosition TownSpot = new(10f, 0f, -3f);

    // The western edge of the interest cell the field's portal lands a player in: the Quartermaster's cell, west of
    // it, is only in view from this side (interest cells of 16 m, the neighbours in view).
    private const float QuartermasterInViewWestOf = 16f;

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

    // The server's own events down to Debug, the audit's refusals included, go to the capture; the console keeps
    // Information.
    private IHost StartHost(string contentRootPath, CapturingLoggerProvider logs)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=5",
                "--Logging:LogLevel:Evertorch=Debug"
            },
            contentRootPath,
            m_database.ConnectionString);
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0));
        builder.Logging.AddProvider(logs);
        builder.Logging.AddFilter<ConsoleLoggerProvider>("Evertorch", LogLevel.Information);
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    /// <summary>
    ///     The player enters the training ground, crosses to the training field, kills five forest crawlers one at a
    ///     time, crosses back, and stops in town; the server then stops. Returns what the server and the client last
    ///     showed of the character.
    /// </summary>
    private static Stopped PlayTheLoop(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;

        using var client = new SocketClient(content, Identity, CharacterName);
        client.EnterWorld(port);
        Assert.That(client.Connection.Characters.Single().Name, Is.EqualTo(CharacterName), "enter: created");
        Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "enter: in town");
        Assert.That(client.World.Inventory.Rows, Is.Empty, "enter: a new character carries nothing");
        Assert.That((client.World.Level, client.World.Experience), Is.EqualTo(((ushort)1, 0ul)), "enter: level 1");
        Assert.That(client.World.Inventory.Coins, Is.Zero, "enter: a new character holds no coins");

        MeetTheNpcs(content, admin, client);
        CrossToTheField(content, admin, client);
        HuntCrawlers(content, admin, client);
        ReturnToTown(content, admin, client);
        TradeAtTheQuartermaster(content, admin, client);

        var stopped = new Stopped(
            SummaryOf(admin, client.World.LocalEntity),
            RowsOf(client.World));
        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected),
            Is.True,
            "stop: the client heard the server stop");
        Assert.That(client.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Maintenance), "stop");
        AssertCleanTraffic(client, "loop");
        return stopped;
    }

    /// <summary>
    ///     After a clean stop and a second server on the same database, the player signs in again to the same
    ///     character in town, where it stood, at the level and experience it reached, with the same inventory and the
    ///     sword and armor still worn.
    /// </summary>
    private static void PlayAfterTheRestart(IHost host, Stopped stopped)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        using var client = new SocketClient(content, Identity, CharacterName);
        client.EnterWorld(port);

        Assert.That(client.Connection.Characters.Single().Name, Is.EqualTo(CharacterName), "restart: the same one");
        Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "restart: in town");
        Assert.That(
            client.DistanceTo(stopped.Summary.Position),
            Is.LessThanOrEqualTo(ConvergedDistance),
            "restart: where the shutdown checkpoint left it");
        Assert.That(
            (client.World.Level, client.World.Experience),
            Is.EqualTo(((ushort)stopped.Summary.Level, (ulong)stopped.Summary.Experience)),
            "restart: the client shows the level and experience the hunt gave");
        bool isKept = client.PumpUntil(() =>
        {
            PlayerSummary? now = admin.GetPlayers(AdminActor.LocalConsole)
                .SingleOrDefault(player => player.Entity == client.World.LocalEntity);
            return now != null && now.Level == stopped.Summary.Level && now.Experience == stopped.Summary.Experience;
        });
        Assert.That(isKept, Is.True, "restart: the server kept the level and experience");
        Assert.That(client.World.Inventory.Coins, Is.EqualTo((uint)stopped.Summary.Coins), "restart: the same coins");
        Assert.That(
            SummaryOf(admin, client.World.LocalEntity).Coins,
            Is.EqualTo(stopped.Summary.Coins),
            "restart: the server holds them too");
        Assert.That(RowsOf(client.World), Is.EqualTo(stopped.Rows), "restart: the same inventory, equipment included");
        Assert.That(
            client.World.Inventory.Rows.Where(row => row.Slot != EquipmentSlot.None)
                .Select(row => (row.Item.Value, row.Slot)),
            Is.EquivalentTo(new[] { (TrainingSword, EquipmentSlot.Weapon), (ClothArmor, EquipmentSlot.Armor) }),
            "restart: the sword and the armor are still worn");
        AssertCleanTraffic(client, "restart");
    }

    // At the spawn point both NPCs are in view, each with what it offers (Network Protocol §6, §9). The talk key walks
    // the player up to the nearer one, the Quartermaster, and its window opens; talking sends nothing (Gameplay Systems
    // §6.1). No snapshot ever carries an NPC: each keeps the one position its spawn gave it.
    private static void MeetTheNpcs(ServerContent content, IAdminCommandService admin, SocketClient client)
    {
        const string step = "meet";
        ClientWorld world = client.World;
        int snapshots = world.SnapshotsApplied;
        Assert.That(
            client.PumpUntil(() =>
                IsInViewWithServices(world, Quartermaster) && IsInViewWithServices(world, GateWarden)),
            Is.True,
            $"{step}: both NPCs and their services on entering at the spawn");
        world.TryGetNpcServices(NpcInView(world, Quartermaster)!.Entity, out NpcServices? shop);
        world.TryGetNpcServices(NpcInView(world, GateWarden)!.Entity, out NpcServices? warden);
        string[] traded = content.Npcs[new NpcDefinitionId(Quartermaster)].Shop.Select(stock => stock.Item.Value)
            .Union(content.Items.Values.Where(item => item.SellPrice > 0).Select(item => item.Id.Value))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        Assert.That(shop!.Entries.Select(entry => entry.Item.Value), Is.EqualTo(traded), $"{step}: the shop");
        Assert.That(
            warden!.Offers.Select(offer => offer.Quest.Value),
            Is.EqualTo(new[] { CrawlerHunt }),
            $"{step}: the quest");

        RemoteEntity quartermaster = NpcInView(world, Quartermaster)!;
        Assert.That(client.TalkToNearest(), Is.EqualTo(quartermaster.Entity), $"{step}: the nearer NPC");
        Assert.That(
            client.PumpUntil(() => client.NpcWindows.Contains(quartermaster.Entity)),
            Is.True,
            $"{step}: walked up to the Quartermaster, at {world.Predictor.Position}");
        WorldPosition standing = content.Maps[new MapDefinitionId(TrainingGround)].Npcs
            .Single(placement => placement.Npc.Value == Quartermaster)
            .Position;
        Assert.That(client.DistanceTo(standing), Is.LessThanOrEqualTo(TalkState.OpenDistance), $"{step}: beside it");
        Assert.That(world.Target, Is.EqualTo(default(EntityId)), $"{step}: nothing targeted");
        Assert.That(SummaryOf(admin, world.LocalEntity).RefusedCommands, Is.Zero, $"{step}: nothing refused");
        Assert.That(world.SnapshotsApplied - snapshots, Is.Positive, $"{step}: snapshots came meanwhile");
        AssertNoNpcInASnapshot(world, step);
    }

    // The player walks into the portal before the ground's east gate and follows its character to the field (Gameplay
    // Systems §4.2).
    private static void CrossToTheField(ServerContent content, IAdminCommandService admin, SocketClient client)
    {
        const string step = "leave town";
        var field = new MapDefinitionId(TrainingField);
        MapPortal portal = content.Maps[new MapDefinitionId(TrainingGround)].Portals.Single();
        Assert.That(portal.DestinationMap, Is.EqualTo(field), step);
        Assert.That(
            client.Controller.TryMoveTo(
                client.World.Predictor.Position,
                new WorldPosition(portal.Center.X - 0.3f, 0f, portal.Center.Z)),
            Is.True,
            $"{step}: a way into the portal");
        Assert.That(
            client.PumpUntil(() => client.Connection.World?.Map == field
                && client.Connection.World.Inventory.IsCurrent),
            Is.True,
            $"{step}: the client followed its character to the field");
        AwaitConvergence(admin, step, client);
        Assert.That(SummaryOf(admin, client.World.LocalEntity).Map, Is.EqualTo(field), $"{step}: on the field");
    }

    // At a staging point beyond the reach of any crawler at home, the player walks within the perception of the
    // nearest one and leads it back once it attacks; a crawler already attacking is fought first. Five crawlers die
    // one at a time, the later ones after their respawn (Gameplay Systems §10). The player picks up everything each
    // one drops, wears the first sword and armor, and drinks a potion when below half its HP. Each kill gives the
    // player the whole of the crawler's experience, since nobody else hurt it (Gameplay Systems §2.1).
    private static void HuntCrawlers(ServerContent content, IAdminCommandService admin, SocketClient client)
    {
        const string step = "hunt";
        ClientWorld world = client.World;
        var crawlerId = new MonsterDefinitionId(ForestCrawler);
        MonsterDefinition crawler = content.Monsters[crawlerId];
        MapDefinition field = content.Maps[new MapDefinitionId(TrainingField)];
        WorldPosition home = field.MonsterSpawns.Single(spawn => spawn.Monster == crawlerId).Center;
        WorldPosition arrival = content.Maps[new MapDefinitionId(TrainingGround)].Portals.Single().DestinationPosition;
        WorldPosition staging = Toward(home, arrival, StagingDistance);
        Assert.That(world.Grid.CanOccupy(staging.X, staging.Z), Is.True, $"{step}: the staging point is standable");

        var attacks = new Dictionary<EntityId, Stopwatch>();
        var deaths = new List<EntityId>();
        var drops = new List<ItemDropped>();
        world.AttackStartedReceived += started =>
        {
            if (started.Target == world.LocalEntity && IsCrawler(world, started.Attacker))
            {
                attacks[started.Attacker] = Stopwatch.StartNew();
            }
        };
        world.EntityDiedReceived += died => deaths.Add(died.Entity);
        world.ItemDroppedReceived += drops.Add;

        WalkTo(step, client, staging);
        var progress = new LevelProgress(world.Level, (long)world.Experience);
        ExperienceTableDefinition table =
            content.ExperienceTables[content.Jobs[new JobDefinitionId(Adventurer)].ExperienceTable];
        var rules = new RenewalProgressionRules();
        for (int kill = 1; kill <= HuntedCrawlers; kill++)
        {
            string killStep = $"{step} {kill}";
            EntityId target = CurrentAttacker(world, attacks) ?? Lure(killStep, client, staging, attacks);
            WalkTo(killStep, client, staging);
            int dropsBefore = drops.Count;
            client.Connection.SendTarget(target);
            Assert.That(
                client.PumpUntil(() => world.Target == target),
                Is.True,
                $"{killStep}: the server confirmed the crawler as the target");
            client.AttackTarget();
            Assert.That(client.PumpUntil(() => deaths.Contains(target), FightLimit), Is.True, $"{killStep}: it died");
            Assert.That(world.IsLocalDead, Is.False, $"{killStep}: the player survived");
            client.AutoAttack.OnWalkRequested();
            attacks.Remove(target);
            progress = rules.AddExperience(table, progress, crawler.BaseExperience);

            Assert.That(
                client.PumpUntil(() => drops.Count - dropsBefore == crawler.Drops.Count),
                Is.True,
                $"{killStep}: every entry of its table dropped");
            foreach (ItemDropped drop in drops.Skip(dropsBefore).ToList())
            {
                PickUp(killStep, client, drop);
            }

            if (kill == 1)
            {
                Equip(killStep, client, TrainingSword, EquipmentSlot.Weapon);
                Equip(killStep, client, ClothArmor, EquipmentSlot.Armor);
            }

            DrinkWhenHurt(killStep, client);
        }

        LevelProgress expected = progress;
        bool isAwarded = client.PumpUntil(() =>
        {
            PlayerSummary summary = SummaryOf(admin, world.LocalEntity);
            return summary.Level == expected.Level && summary.Experience == expected.Experience;
        });
        Assert.That(isAwarded, Is.True, $"{step}: the server holds level {expected.Level} at {expected.Experience}");
        Assert.That(
            (world.Level, world.Experience),
            Is.EqualTo(((ushort)expected.Level, (ulong)expected.Experience)),
            $"{step}: the client shows its level and experience");
        Assert.That(
            world.Inventory.Rows.Count(row => row.Item.Value == TrainingSword),
            Is.EqualTo(HuntedCrawlers),
            $"{step}: a sword from every crawler");
    }

    // Each try walks within a crawler's perception and waits there for it to come; aiming again while it comes would
    // only keep the player out of its reach. While every crawler is dead the player waits for a respawn.
    private static EntityId Lure(
        string step,
        SocketClient client,
        WorldPosition staging,
        IReadOnlyDictionary<EntityId, Stopwatch> attacks)
    {
        ClientWorld world = client.World;
        var elapsed = Stopwatch.StartNew();
        var tries = new List<string>();
        EntityId? attacker = null;
        while (attacker == null && elapsed.Elapsed < LureLimit)
        {
            (EntityId Entity, WorldPosition Position)[] drawn = world.Remotes.Values
                .Where(remote => remote.DefinitionId == ForestCrawler && !remote.IsDead)
                .Select(remote => (remote.Entity, Position: Drawn(world, remote)))
                .Where(crawler => crawler.Position != null)
                .Select(crawler => (crawler.Entity, crawler.Position!.Value))
                .OrderBy(crawler => Horizontal(crawler.Value, staging))
                .ToArray();
            if (drawn.Length == 0)
            {
                client.PumpFor(TimeSpan.FromMilliseconds(500));
                continue;
            }

            WorldPosition goal = Toward(drawn[0].Position, staging, LureDistance);
            bool isWalking = client.Controller.TryMoveTo(world.Predictor.Position, goal);
            tries.Add($"{drawn[0].Entity.Value} goal {goal} walking {isWalking}");
            client.PumpUntil(() => CurrentAttacker(world, attacks) != null, TimeSpan.FromSeconds(4));
            attacker = CurrentAttacker(world, attacks);
        }

        Assert.That(attacker, Is.Not.Null, $"{step}: a crawler went for the player; tries: {string.Join("; ", tries)}");
        return attacker!.Value;
    }

    // A live crawler that attacked the player within the last few seconds.
    private static EntityId? CurrentAttacker(ClientWorld world, IReadOnlyDictionary<EntityId, Stopwatch> attacks)
    {
        foreach (KeyValuePair<EntityId, Stopwatch> attack in attacks.OrderBy(pair => pair.Value.Elapsed))
        {
            if (attack.Value.Elapsed < AttackMemory && IsAlive(world, attack.Key))
            {
                return attack.Key;
            }
        }

        return null;
    }

    // The pickup state walks up to the drop and picks it up; the row's quantity then grows by the drop's amount.
    private static void PickUp(string step, SocketClient client, ItemDropped drop)
    {
        ClientWorld world = client.World;
        uint before = Held(world, drop.ItemId);
        client.Pickup.Pickup(drop.Entity);
        Assert.That(
            client.PumpUntil(() => Held(world, drop.ItemId) == before + drop.Amount),
            Is.True,
            $"{step}: picked up {drop.Amount} {drop.ItemId}");
    }

    // Back in town the player walks up to the Quartermaster and sells what the hunt brought back but the potions and
    // what it wears, then buys a new training sword and cloth armor and wears them (Gameplay Systems §11.3). Each
    // trade answers with the coins, one operation at a time.
    private static void TradeAtTheQuartermaster(ServerContent content, IAdminCommandService admin, SocketClient client)
    {
        const string step = "trade";
        ClientWorld world = client.World;
        RemoteEntity quartermaster = NpcInView(world, Quartermaster)!;
        int windows = client.NpcWindows.Count;
        client.TalkTo(quartermaster.Entity);
        Assert.That(client.PumpUntil(() => client.NpcWindows.Count > windows), Is.True, $"{step}: walked up to it");

        uint earned = 0;
        foreach (InventoryEntry row in world.Inventory.Rows
                     .Where(row => row.Slot == EquipmentSlot.None && row.Item.Value != MinorHealth)
                     .ToList())
        {
            uint fetches = row.Quantity * (uint)content.Items[row.Item].SellPrice;
            uint before = world.Inventory.Coins;
            client.Connection.SendSell(quartermaster.Entity, row.InventoryItem, row.Quantity);
            Assert.That(
                client.PumpUntil(() => world.Inventory.Coins == before + fetches
                    && world.Inventory.Rows.All(held => held.InventoryItem != row.InventoryItem)),
                Is.True,
                $"{step}: sold {row.Quantity} {row.Item.Value} for {fetches}; {world.LastRejection}");
            earned += fetches;
        }

        Assert.That(earned, Is.GreaterThanOrEqualTo(90u), $"{step}: enough for a sword and armor");
        long sword = Buy(step, content, client, quartermaster.Entity, TrainingSword);
        long armor = Buy(step, content, client, quartermaster.Entity, ClothArmor);
        EquipRow(step, client, sword, EquipmentSlot.Weapon);
        EquipRow(step, client, armor, EquipmentSlot.Armor);

        Assert.That(world.Inventory.Coins, Is.EqualTo(earned - 90u), $"{step}: 50 for the sword and 40 for the armor");
        bool isAgreed = client.PumpUntil(() => SummaryOf(admin, world.LocalEntity).Coins == world.Inventory.Coins);
        Assert.That(isAgreed, Is.True, $"{step}: the server holds the same coins");
    }

    // Buys one of the item and returns the row it came in.
    private static long Buy(string step, ServerContent content, SocketClient client, EntityId npc, string item)
    {
        ClientWorld world = client.World;
        uint price = (uint)content.Npcs[new NpcDefinitionId(Quartermaster)].Shop
            .Single(stock => stock.Item.Value == item)
            .Price;
        uint before = world.Inventory.Coins;
        var held = new HashSet<long>(world.Inventory.Rows.Select(row => row.InventoryItem));
        client.Connection.SendBuy(npc, new ItemDefinitionId(item), 1);
        Assert.That(
            client.PumpUntil(() => world.Inventory.Coins == before - price
                && world.Inventory.Rows.Any(row => !held.Contains(row.InventoryItem))),
            Is.True,
            $"{step}: bought {item} for {price}; {world.LastRejection}");
        return world.Inventory.Rows.Single(row => !held.Contains(row.InventoryItem)).InventoryItem;
    }

    private static void EquipRow(string step, SocketClient client, long row, EquipmentSlot slot)
    {
        ClientWorld world = client.World;
        client.Connection.SendEquip(row);
        Assert.That(
            client.PumpUntil(() => world.Inventory.Rows.Any(entry => entry.InventoryItem == row && entry.Slot == slot)),
            Is.True,
            $"{step}: row {row} worn");
    }

    private static void Equip(string step, SocketClient client, string item, EquipmentSlot slot)
    {
        ClientWorld world = client.World;
        long row = world.Inventory.Rows.First(entry => entry.Item.Value == item).InventoryItem;
        client.Connection.SendEquip(row);
        Assert.That(
            client.PumpUntil(() => world.Inventory.Rows.Any(entry => entry.InventoryItem == row && entry.Slot == slot)),
            Is.True,
            $"{step}: {item} worn");
    }

    private static void DrinkWhenHurt(string step, SocketClient client)
    {
        ClientWorld world = client.World;
        InventoryEntry? potion = world.Inventory.Rows
            .Where(row => row.Item.Value == MinorHealth)
            .Select(row => (InventoryEntry?)row)
            .FirstOrDefault();
        if (world.LocalHealth * 2 >= world.LocalMaximumHealth || potion == null)
        {
            return;
        }

        uint before = world.LocalHealth;
        uint held = potion.Value.Quantity;
        client.Connection.SendUseItem(potion.Value.InventoryItem);
        Assert.That(
            client.PumpUntil(() => Held(world, MinorHealth) == held - 1 && world.LocalHealth > before),
            Is.True,
            $"{step}: a potion drunk and HP back from {before}");
    }

    // The player walks into the portal before the field's west gate and follows its character back to the ground,
    // then walks into town, where the client agrees with the server.
    private static void ReturnToTown(ServerContent content, IAdminCommandService admin, SocketClient client)
    {
        const string step = "return";
        var ground = new MapDefinitionId(TrainingGround);
        MapPortal portal = content.Maps[new MapDefinitionId(TrainingField)].Portals.Single();
        Assert.That(portal.DestinationMap, Is.EqualTo(ground), step);
        Assert.That(
            client.Controller.TryMoveTo(
                client.World.Predictor.Position,
                new WorldPosition(portal.Center.X + 0.3f, 0f, portal.Center.Z)),
            Is.True,
            $"{step}: a way into the portal");
        Assert.That(
            client.PumpUntil(() => client.Connection.World?.Map == ground
                && client.Connection.World.Inventory.IsCurrent),
            Is.True,
            $"{step}: the client followed its character back to the ground");
        Assert.That(client.Connection.MapEpoch, Is.EqualTo(2), $"{step}: two crossings");

        // The Quartermaster's interest cell is out of view from the arrival, and comes into view on the walk into
        // town.
        ClientWorld town = client.World;
        Assert.That(
            client.PumpUntil(() => IsInViewWithServices(town, GateWarden)),
            Is.True,
            $"{step}: the Gate Warden and its services on arrival");
        Assert.That(NpcInView(town, Quartermaster), Is.Null, $"{step}: the Quartermaster out of view at the arrival");
        float? seenFrom = null;
        town.RemoteSpawned += remote =>
        {
            if (remote.DefinitionId == Quartermaster)
            {
                seenFrom ??= town.Predictor.Position.X;
            }
        };

        WalkTo(step, client, TownSpot);
        AwaitConvergence(admin, step, client);
        Assert.That(SummaryOf(admin, client.World.LocalEntity).Map, Is.EqualTo(ground), $"{step}: in town");
        Assert.That(IsInViewWithServices(town, Quartermaster), Is.True, $"{step}: the Quartermaster in town");
        Assert.That(
            seenFrom,
            Is.LessThan(QuartermasterInViewWestOf),
            $"{step}: the Quartermaster came into view once the player walked west of x = 16");
        AssertNoNpcInASnapshot(town, step);
    }

    // Starts the walk, then pumps until the walker has arrived within a step of its goal.
    private static void WalkTo(string step, SocketClient client, WorldPosition goal)
    {
        Assert.That(
            client.Controller.TryMoveTo(client.World.Predictor.Position, goal),
            Is.True,
            $"{step}: a way to {goal}");
        Assert.That(
            client.PumpUntil(() => !client.Controller.HasPath && client.DistanceTo(goal) < 0.5f),
            Is.True,
            $"{step}: arrived at {goal}, at {client.World.Predictor.Position}");
    }

    private static void AwaitConvergence(IAdminCommandService admin, string step, SocketClient client)
    {
        // The admin view is republished once a second, so agreement shows up within two of those.
        bool isConverged = client.PumpUntil(() =>
        {
            PlayerSummary? summary = admin.GetPlayers(AdminActor.LocalConsole)
                .FirstOrDefault(player => player.Entity == client.World.LocalEntity);
            return client.World.Predictor.PendingCount == 0
                && summary != null
                && client.DistanceTo(summary.Position) <= ConvergedDistance;
        });
        Assert.That(
            isConverged,
            Is.True,
            $"{step}: the client at {client.World.Predictor.Position} agrees with the server at "
            + SummaryOf(admin, client.World.LocalEntity).Position);
    }

    // The point at `distance` from `from` toward `toward`, on the ground.
    private static WorldPosition Toward(WorldPosition from, WorldPosition toward, float distance)
    {
        float length = Horizontal(from, toward);
        return new WorldPosition(
            from.X + (toward.X - from.X) / length * distance,
            0f,
            from.Z + (toward.Z - from.Z) / length * distance);
    }

    private static RemoteEntity? NpcInView(ClientWorld world, string npc)
    {
        return world.Remotes.Values.SingleOrDefault(remote =>
            remote.Kind == EntityKind.Npc && remote.DefinitionId == npc);
    }

    private static bool IsInViewWithServices(ClientWorld world, string npc)
    {
        RemoteEntity? remote = NpcInView(world, npc);
        return remote != null && world.TryGetNpcServices(remote.Entity, out NpcServices? _);
    }

    // A snapshot that carried an NPC would add a sample after the one its spawn gave it.
    private static void AssertNoNpcInASnapshot(ClientWorld world, string step)
    {
        Assert.That(
            world.Remotes.Values.Where(remote => remote.Kind == EntityKind.Npc).Select(remote => remote.Buffer.Count),
            Is.Not.Empty.And.All.EqualTo(1),
            $"{step}: no NPC in a snapshot");
    }

    private static bool IsCrawler(ClientWorld world, EntityId entity)
    {
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote) && remote.DefinitionId == ForestCrawler;
    }

    private static bool IsAlive(ClientWorld world, EntityId entity)
    {
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote) && !remote.IsDead;
    }

    private static uint Held(ClientWorld world, string item)
    {
        return (uint)world.Inventory.Rows.Where(row => row.Item.Value == item).Sum(row => row.Quantity);
    }

    private static WorldPosition? Drawn(ClientWorld world, RemoteEntity remote)
    {
        return remote.Buffer.TrySample(world.RemoteRenderTime, out WorldPosition position, out WorldDirection _)
            ? position
            : null;
    }

    private static float Horizontal(WorldPosition a, WorldPosition b)
    {
        float deltaX = a.X - b.X;
        float deltaZ = a.Z - b.Z;
        return (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static PlayerSummary SummaryOf(IAdminCommandService admin, EntityId entity)
    {
        return admin.GetPlayers(AdminActor.LocalConsole).Single(player => player.Entity == entity);
    }

    // Every row as the client holds it, in row order.
    private static List<string> RowsOf(ClientWorld world)
    {
        return world.Inventory.Rows
            .OrderBy(row => row.InventoryItem)
            .Select(row => $"{row.InventoryItem} {row.Item.Value} x{row.Quantity} {row.Slot}")
            .ToList();
    }

    private static void AssertCleanTraffic(SocketClient client, string step)
    {
        Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
        Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
    }

    // Every sale and purchase of the loop is one ledger row, and together they account for the coins the character
    // holds (Persistence §5).
    private void AssertTheTradesInTheLedger(Stopped stopped)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT operation_type, currency_delta FROM economy_ledger "
            + "WHERE actor_character_id = @character AND operation_type IN ('buy', 'sell') ORDER BY id",
            connection);
        command.Parameters.AddWithValue("character", stopped.Summary.Character.Value);
        var trades = new List<(string Type, long Coins)>();
        using (NpgsqlDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                trades.Add((reader.GetString(0), reader.GetInt64(1)));
            }
        }

        Assert.That(trades.Count(trade => trade.Type == "buy"), Is.EqualTo(2), "ledger: the sword and the armor");
        Assert.That(trades.Where(trade => trade.Type == "sell").Select(trade => trade.Coins), Is.All.Positive);
        Assert.That(trades.Sum(trade => trade.Coins), Is.EqualTo(stopped.Summary.Coins), "ledger: the coins held");
    }

    private sealed class Stopped
    {
        public Stopped(PlayerSummary summary, List<string> rows)
        {
            Summary = summary;
            Rows = rows;
        }

        public PlayerSummary Summary { get; }

        public List<string> Rows { get; }
    }

    [Test]
    public void TownLoop_OverRealSocketsAndPostgres_LeavesTownHuntsReturnsAndKeepsTheResults()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        var logs = new CapturingLoggerProvider();
        Stopped stopped;
        using (IHost first = StartHost(root.Path, logs))
        {
            stopped = PlayTheLoop(first);
            AssertTheTradesInTheLedger(stopped);
        }

        using (IHost restarted = StartHost(root.Path, logs))
        {
            PlayAfterTheRestart(restarted, stopped);
            restarted.StopAsync().GetAwaiter().GetResult();
        }

        // Nothing the whole loop logged names the database's connection string or password or the player's identity.
        IReadOnlyList<string> lines = logs.Lines;
        Assert.That(lines.Any(line => line.Contains("MapTransferred")), Is.True, "logs: the loop was captured");
        Assert.That(
            lines.Where(line => line.Contains(m_database.ConnectionString)
                || line.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || line.Contains(Identity)),
            Is.Empty,
            "logs: no secret");
    }
}
}
