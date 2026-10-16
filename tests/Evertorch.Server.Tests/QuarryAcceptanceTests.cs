using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 21 "Ashen Quarry" exit criterion's server side end to end (ROADMAP §8): the composed server host
///     on a real PostgreSQL 18 and real UDP sockets on loopback, loading the server-only test package
///     (<see cref="DungeonPackage" />) with the quarry's spawns, and four clients built from the client's production
///     networking and gameplay code. Kael and Tarn, Vanguards, and Ilsa, an Arcanist, each stored at base level 25 and
///     job level 10 with the balance baseline's build and the complete +0 dungeon set (Prototype Content §6), enter the
///     training ground together: the Smith's fees for seventeen cards come in two parts, the Gate Warden offers five
///     quests, and the three form a party and take the quarry's two. Through the field's northern gate they enter the
///     Ashen Quarry, where a Cinder Crawler's fall counts for every member's hunt, a Basalt Crawler's partner answers its
///     call, and an Ash Wisp silences Ilsa, each dropping its card; in the arena the Furnace Heart's burst spares Tarn,
///     who steps out, and strikes Kael, the boss falls, its most valuable player alone has ten Emberstones, and Ivor, in
///     town, hears none of it. Back in town the members turn their quests in, Kael cards his armor and cape, Tarn sees
///     both cards, Kael takes one out at the Smith and trades it to Tarn, who stores it, and Kael lists another on the
///     board; after a clean stop a second host on the same database finds all of it where it was left.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class QuarryAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingField = "map.training_field";
    private const string AshenQuarry = "map.ashen_quarry";
    private const string GateWarden = "npc.gate_warden";
    private const string Smith = "npc.smith";
    private const string Storekeeper = "npc.storekeeper";
    private const string Broker = "npc.broker";
    private const string CinderCrawler = "monster.cinder_crawler";
    private const string BasaltCrawler = "monster.basalt_crawler";
    private const string AshWisp = "monster.ash_wisp";
    private const string FurnaceHeart = "monster.furnace_heart";
    private const string QuarryHunt = "quest.quarry_hunt";
    private const string BossQuest = "quest.furnace_heart";
    private const string Emberstone = "item.material.emberstone";
    private const string FirewardCard = "item.card.cinder_crawler";
    private const string SteadyCard = "item.card.basalt_crawler";
    private const string DousingCard = "item.card.ash_wisp";
    private const string MoltenCard = "item.card.furnace_heart";
    private const string IvorIdentity = "quarry-ivor";
    private const string IvorName = "Ivor";
    private const uint KaelCoins = 20_000;
    private const string Vanguard = "job.vanguard";
    private const string Arcanist = "job.arcanist";
    private const string KaelIdentity = "quarry-kael";
    private const string KaelName = "Kael";
    private const string TarnIdentity = "quarry-tarn";
    private const string TarnName = "Tarn";
    private const string IlsaIdentity = "quarry-ilsa";
    private const string IlsaName = "Ilsa";

    private static readonly TimeSpan WalkLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);
    private static readonly SkillDefinitionId FurnaceBurst = new("skill.furnace_burst");

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

    // Under the training field's north wall, two metres in front of its northern gate, where an arrival from the
    // quarry stands.
    private static readonly WorldPosition UnderTheNorthWall = new(-12f, 0f, 20.5f);

    // In the quarry: the crossroads; the cinder basin, within a Cinder Crawler's notice; the east road; the southern
    // of the basalt galleries' alcoves; and the eastern ash terrace.
    private static readonly WorldPosition AtTheCrossroads = new(0f, 0f, -5f);
    private static readonly WorldPosition InTheBasin = new(-21f, 0f, -13f);
    private static readonly WorldPosition ThroughTheEastRoad = new(16f, 0f, -5f);
    private static readonly WorldPosition InTheSouthAlcove = new(28.5f, 0f, -27f);
    private static readonly WorldPosition OnTheEastTerrace = new(24f, 0f, 20f);

    // Kael 2.5 m south of the boss's home at (0, 28); Tarn and Ilsa 9.5 m from it, beyond its 6 m of notice; and Tarn's
    // place inside the burst's 5 m.
    private static readonly WorldPosition BeforeTheBoss = new(0f, 0f, 25.5f);
    private static readonly WorldPosition HangingBack = new(-3f, 0f, 19f);
    private static readonly WorldPosition WaitingBack = new(3f, 0f, 19f);
    private static readonly WorldPosition BesideTheBoss = new(-2f, 0f, 25f);

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

    // No reconnect grace, so a player who leaves is out of the world at once. Every hit and every ailment lands, and
    // every drop entry drops at its least amount, since network timing decides which draw each roll gets (Coding
    // Standards §10).
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
        TestHosts.ScriptOutcomes(builder, new Landing(), new ScriptedRandom(0), new ScriptedRandom(99));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private void EnterTogether(int port, SocketClient kael, SocketClient tarn, SocketClient ilsa)
    {
        const string step = "enter";
        kael.AfterCreate = () =>
        {
            Store(KaelName, VanguardBuild, VanguardStats, "item.weapon.iron_sword");
            foreach (string card in new[] { MoltenCard, FirewardCard, SteadyCard })
            {
                StoreWorn(KaelName, card, EquipmentSlot.None);
            }

            Execute($"UPDATE characters SET currency = {KaelCoins} WHERE name = '{KaelName}'");
        };
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
        Assert.That(cards, Is.EqualTo(17), $"{step}: the cards in content, in two parts");
        Assert.That(world.DroppedFeeLists, Is.Zero, $"{step}: no list of fees dropped");

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
            Is.EqualTo(
                new[]
                {
                    "quest.crawler_hunt", "quest.furnace_heart", "quest.grotto_hunt", "quest.quarry_hunt",
                    "quest.slime_monarch"
                }),
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

    // The party walks up under the field's north wall, short of the portal before its gate.
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

    // Gameplay Systems §4.2: the party walks into the portal before the field's northern gate and follows its
    // characters into the quarry's arrival yard.
    private static void CrossToTheQuarry(ServerContent content, params SocketClient[] party)
    {
        var quarry = new MapDefinitionId(AshenQuarry);
        MapPortal north = content.Maps[new MapDefinitionId(TrainingField)].Portals
            .Single(portal => portal.DestinationMap == quarry);
        Cross("to the quarry", new WorldPosition(north.Center.X, 0f, north.Center.Z - 0.3f), quarry, party);
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.DistanceTo(content.Maps[quarry].SpawnPosition),
                Is.LessThan(0.5f),
                "to the quarry: in its arrival yard");
        }
    }

    private static void Cross(
        string step,
        WorldPosition intoThePortal,
        MapDefinitionId destination,
        params SocketClient[] party)
    {
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.Controller.TryMoveTo(client.World.Predictor.Position, intoThePortal),
                Is.True,
                $"{step}: a way into the portal");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => party.All(client => client.Connection.World?.Map == destination
                    && client.Connection.World.Inventory.IsCurrent),
                WalkLimit,
                party),
            Is.True,
            $"{step}: every member followed its character");
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

    // An item of the character's, worn in the slot unless it is None.
    private void StoreWorn(string name, string item, EquipmentSlot slot)
    {
        if (slot == EquipmentSlot.None)
        {
            Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"SELECT id, '{item}', 1, 0, 0 FROM characters WHERE name = '{name}'");
            return;
        }

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

    // Kael invites Tarn, then Ilsa; once both accept, each of the three hears the roster of three that Kael leads
    // (Gameplay Systems §14).
    private static void FormParty(SocketClient kael, SocketClient tarn, SocketClient ilsa)
    {
        const string step = "party";
        SocketClient[] all = { kael, tarn, ilsa };
        var heard = new Dictionary<SocketClient, List<string>>();
        var rosters = new Dictionary<SocketClient, PartyRoster>();
        foreach (SocketClient client in all)
        {
            var events = new List<string>();
            heard.Add(client, events);
            client.Connection.PartyEventReceived += partyEvent => events.Add($"{partyEvent.Kind} {partyEvent.Name}");
            client.Connection.PartyRosterReceived += roster => rosters[client] = roster;
        }

        foreach ((SocketClient invitee, string name) in new[] { (tarn, TarnName), (ilsa, IlsaName) })
        {
            kael.Connection.SendPartyInvite(name);
            Assert.That(
                SocketClients.PumpUntil(() => heard[invitee].Contains($"Invited {KaelName}"), all),
                Is.True,
                $"{step}: {name} invited");
            invitee.Connection.SendPartyReply(KaelName, true);
            Assert.That(
                SocketClients.PumpUntil(() => heard[kael].Contains($"Joined {name}"), all),
                Is.True,
                $"{step}: {name} joined");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => all.All(client => rosters.TryGetValue(client, out PartyRoster? roster)
                    && roster.Members.Count == 3),
                all),
            Is.True,
            $"{step}: every member heard the roster of three");
    }

    private static string QuestOf(ClientWorld world, string quest)
    {
        return world.Quests
            .Where(entry => entry.Quest.Value == quest)
            .Select(entry => $"{entry.State} {entry.Progress}/{entry.Count}")
            .SingleOrDefault() ?? string.Empty;
    }

    // Each member walks up to the Gate Warden and takes the quarry's two quests (Gameplay Systems §2.2): the quest log
    // answers both active, nothing counted. The test package's hunt asks for one crawler.
    private static void AcceptTheQuarryQuests(params SocketClient[] party)
    {
        const string step = "the quests";
        foreach (SocketClient client in party)
        {
            EntityId warden = WalkUpTo(step, GateWarden, client, party);
            client.Connection.SendAcceptQuest(warden, new QuestDefinitionId(QuarryHunt));
            client.Connection.SendAcceptQuest(warden, new QuestDefinitionId(BossQuest));
            Assert.That(
                SocketClients.PumpUntil(
                    () => QuestOf(client.World, QuarryHunt) == "Active 0/1"
                        && QuestOf(client.World, BossQuest) == "Active 0/1",
                    party),
                Is.True,
                $"{step}: both taken; {client.World.LastRejection}");
        }
    }

    private static bool IsOf(ClientWorld world, EntityId entity, string monster)
    {
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote) && remote.DefinitionId == monster;
    }

    private static void WalkTogether(string step, WorldPosition place, params SocketClient[] party)
    {
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.Controller.TryMoveTo(client.World.Predictor.Position, place),
                Is.True,
                $"{step}: a way to ({place.X}, {place.Z})");
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => !client.Controller.HasPath), WalkLimit, party),
            Is.True,
            $"{step}: every member arrived at ({place.X}, {place.Z})");
    }

    // The party brings down what the given member fought, every member at it; the drops it leaves are returned.
    private static List<ItemDropped> BringDown(
        string step,
        EntityId monster,
        SocketClient first,
        params SocketClient[] party)
    {
        var drops = new List<ItemDropped>();
        var deaths = new List<EntityDied>();
        first.World.ItemDroppedReceived += drops.Add;
        first.World.EntityDiedReceived += deaths.Add;
        foreach (SocketClient client in party)
        {
            client.Connection.SendTarget(monster);
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => client.World.Target == monster), party),
            Is.True,
            $"{step}: every member targeted it");
        foreach (SocketClient client in party)
        {
            client.AttackTarget();
        }

        Assert.That(
            SocketClients.PumpUntil(() => deaths.Any(death => death.Entity == monster), FightLimit, party),
            Is.True,
            $"{step}: it fell");
        SocketClients.PumpFor(TimeSpan.FromMilliseconds(300), party);
        first.World.ItemDroppedReceived -= drops.Add;
        first.World.EntityDiedReceived -= deaths.Add;
        foreach (SocketClient client in party)
        {
            client.AutoAttack.OnWalkRequested();
        }

        return drops;
    }

    // In the cinder basin a Cinder Crawler, aggressive, takes the party on; its fall counts for every member's Quarry
    // Hunt, and its card drops (Gameplay Systems §2.2, §11.7). The test package leaves it 100 HP, no dodge, and no
    // swing worth the name; every drop entry drops.
    private static void HuntACinderCrawler(SocketClient kael, params SocketClient[] party)
    {
        const string step = "the hunt";
        WalkTogether(step, InTheBasin, party);
        EntityId crawler = default;
        Assert.That(
            SocketClients.PumpUntil(
                () => IsOf(kael.World, crawler = kael.CycleTarget(true), CinderCrawler),
                FightLimit,
                party),
            Is.True,
            $"{step}: Tab found a Cinder Crawler");
        List<ItemDropped> drops = BringDown(step, crawler, kael, party);

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => QuestOf(client.World, QuarryHunt) == "Active 1/1"),
                party),
            Is.True,
            $"{step}: it counted for every member's Quarry Hunt");
        Assert.That(drops.Select(drop => drop.ItemId), Has.Member(FirewardCard), $"{step}: its card dropped");
    }

    // In the southern alcove Kael attacks a Basalt Crawler and its partner, within 6 m, answers its call, so two swing
    // at the party; the party brings both down, and their card drops (Gameplay Systems §10).
    private static void MeetTheBasaltPair(SocketClient kael, params SocketClient[] party)
    {
        const string step = "the pair";
        var swingers = new HashSet<EntityId>();
        EntityId[] members = party.Select(client => client.World.LocalEntity).ToArray();
        kael.World.AttackStartedReceived += started =>
        {
            if (members.Contains(started.Target) && IsOf(kael.World, started.Attacker, BasaltCrawler))
            {
                swingers.Add(started.Attacker);
            }
        };
        WalkTogether(step, ThroughTheEastRoad, party);
        WalkTogether(step, InTheSouthAlcove, party);
        EntityId first = kael.CycleTarget(true);
        Assert.That(IsOf(kael.World, first, BasaltCrawler), Is.True, $"{step}: Tab found a Basalt Crawler");
        Assert.That(
            SocketClients.PumpUntil(() => kael.World.Target == first, party),
            Is.True,
            $"{step}: the server confirmed the target");
        kael.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(() => swingers.Count >= 2, FightLimit, party),
            Is.True,
            $"{step}: its partner answered the call");
        var drops = swingers.ToArray()
            .SelectMany(basalt => BringDown(step, basalt, kael, party))
            .ToList();

        Assert.That(drops.Select(drop => drop.ItemId), Has.Member(SteadyCard), $"{step}: their card dropped");
    }

    // On the eastern terrace Ilsa attacks an Ash Wisp, which silences her (Gameplay Systems §9.1); the party brings it
    // down and its card drops. The test package has the wisp cast Hush whenever it may.
    private static void MeetTheAshWisp(SocketClient ilsa, params SocketClient[] party)
    {
        const string step = "the wisp";
        WalkTogether(step, ThroughTheEastRoad, party);
        WalkTogether(step, OnTheEastTerrace, party);
        EntityId wisp = ilsa.CycleTarget(true);
        Assert.That(IsOf(ilsa.World, wisp, AshWisp), Is.True, $"{step}: Tab found an Ash Wisp");
        Assert.That(
            SocketClients.PumpUntil(() => ilsa.World.Target == wisp, party),
            Is.True,
            $"{step}: the server confirmed the target");
        // A hit provokes the wisp; Ilsa then stands, so it keeps its distance and casts rather than walking away.
        bool hasHit = false;
        ilsa.World.DamageReceived += damage =>
            hasHit |= damage.Source == ilsa.World.LocalEntity
                && damage.Target == wisp
                && damage.Result != CombatResult.Miss;
        ilsa.AttackTarget();
        Assert.That(SocketClients.PumpUntil(() => hasHit, FightLimit, party), Is.True, $"{step}: Ilsa hit the wisp");
        ilsa.AutoAttack.OnWalkRequested();
        Assert.That(
            SocketClients.PumpUntil(
                () => ilsa.World.StatusEffects.Any(effect => effect.Status.Value == "status.silenced"),
                FightLimit,
                party),
            Is.True,
            $"{step}: the wisp silenced Ilsa");
        List<ItemDropped> drops = BringDown(step, wisp, ilsa, party);

        Assert.That(drops.Select(drop => drop.ItemId), Has.Member(DousingCard), $"{step}: its card dropped");
    }

    // The party walks up the north road into the arena. Kael goes in first, and the Furnace Heart takes Kael for its
    // target; then Tarn comes beside it, and once the client sees its burst's cast begin, Tarn walks out of the 5 m and
    // is spared while Kael, who stays, is struck (Gameplay Systems §9). The party brings the boss down and each member
    // hears it fall, naming its most valuable player, who alone hears its award, ten Emberstones in its bag (its 3,750
    // experience is nothing at the level cap of 25); Ivor, in town, hears none of it, nor the boss's return when the
    // console brings it back (Network Protocol §9). The test package leaves the boss 60 HP and no dodge, has its swings
    // and its burst strike for 1, and has it burst whenever it may.
    private static string MeetTheFurnaceHeart(
        IAdminCommandService admin,
        SocketClient ivor,
        SocketClient kael,
        SocketClient tarn,
        SocketClient ilsa)
    {
        const string step = "the boss";
        SocketClient[] party = { kael, tarn, ilsa };
        SocketClient[] everyone = { kael, tarn, ilsa, ivor };
        var names = new Dictionary<SocketClient, string> { [kael] = KaelName, [tarn] = TarnName, [ilsa] = IlsaName };
        var heard = new Dictionary<SocketClient, List<string>>();
        var awards = new Dictionary<SocketClient, List<MvpAwarded>>();
        foreach (SocketClient client in everyone)
        {
            var lines = new List<string>();
            var awarded = new List<MvpAwarded>();
            heard.Add(client, lines);
            awards.Add(client, awarded);
            client.Connection.BossAnnouncementReceived += announcement =>
                lines.Add($"{announcement.Kind} {announcement.Monster.Value} {announcement.Name}".TrimEnd());
            client.Connection.MvpAwardedReceived += awarded.Add;
        }

        WalkTogether(step, ThroughTheEastRoad, party);
        WalkTogether(step, AtTheCrossroads, party);
        foreach ((SocketClient client, WorldPosition place) in new[]
                 {
                     (kael, BeforeTheBoss), (tarn, HangingBack), (ilsa, WaitingBack)
                 })
        {
            Assert.That(
                client.Controller.TryMoveTo(client.World.Predictor.Position, place),
                Is.True,
                $"{step}: a way into the arena");
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => !client.Controller.HasPath), WalkLimit, everyone),
            Is.True,
            $"{step}: in the arena");
        EntityId boss = kael.World.Remotes.Values.Single(remote => remote.DefinitionId == FurnaceHeart).Entity;
        bool isKaelFought = false;
        kael.World.AttackStartedReceived += started =>
            isKaelFought |= started.Attacker == boss && started.Target == kael.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => isKaelFought, FightLimit, everyone),
            Is.True,
            $"{step}: the boss swung at Kael");
        Assert.That(
            tarn.Controller.TryMoveTo(tarn.World.Predictor.Position, BesideTheBoss),
            Is.True,
            $"{step}: a way beside the boss");
        Assert.That(
            SocketClients.PumpUntil(() => !tarn.Controller.HasPath, everyone),
            Is.True,
            $"{step}: Tarn beside the boss");

        bool hasBurstBegun = false;
        bool hasSteppedOut = false;
        bool isKaelStruck = false;
        bool isTarnStruck = false;
        tarn.World.SkillCastStartedReceived += started =>
            hasBurstBegun |= started.Caster == boss && started.Skill == FurnaceBurst;
        kael.World.SkillResolvedReceived += resolved =>
            isKaelStruck |= resolved.Skill == FurnaceBurst && resolved.Target == kael.World.LocalEntity;
        tarn.World.SkillResolvedReceived += resolved =>
            isTarnStruck |= resolved.Skill == FurnaceBurst && resolved.Target == tarn.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(
                () =>
                {
                    if (hasBurstBegun && !hasSteppedOut)
                    {
                        hasSteppedOut = tarn.Controller.TryMoveTo(tarn.World.Predictor.Position, HangingBack);
                    }

                    return hasSteppedOut && isKaelStruck;
                },
                FightLimit,
                everyone),
            Is.True,
            $"{step}: a burst began, Tarn stepped out, and it struck Kael");
        SocketClients.PumpFor(TimeSpan.FromSeconds(0.5), everyone);
        Assert.That(isTarnStruck, Is.False, $"{step}: the burst spared Tarn");

        var drops = new List<ItemDropped>();
        kael.World.ItemDroppedReceived += drops.Add;
        foreach (SocketClient client in party)
        {
            client.Connection.SendTarget(boss);
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => client.World.Target == boss), everyone),
            Is.True,
            $"{step}: every member targeted the boss");
        foreach (SocketClient client in party)
        {
            client.AttackTarget();
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => party.All(client => heard[client].Any(line => line.StartsWith($"Fell {FurnaceHeart} "))),
                FightLimit,
                everyone),
            Is.True,
            $"{step}: every member heard the boss fall, naming its most valuable player");
        string fell = heard[kael].Single(line => line.StartsWith($"Fell {FurnaceHeart} "));
        SocketClient mvp = party.Single(client => fell == $"Fell {FurnaceHeart} {names[client]}");
        Assert.That(party.All(client => heard[client].Contains(fell)), Is.True, $"{step}: one name for everyone");
        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => QuestOf(client.World, BossQuest) == "Active 1/1"),
                everyone),
            Is.True,
            $"{step}: the fall counted for every member's quest");
        Assert.That(
            SocketClients.PumpUntil(() => awards[mvp].Count > 0, FightLimit, everyone),
            Is.True,
            $"{step}: {names[mvp]} heard the award");
        MvpAwarded award = awards[mvp].Single();
        Assert.That(
            (award.Monster.Value, award.MvpExperience, award.Item?.Value, award.Amount, award.Placed),
            Is.EqualTo((FurnaceHeart, 0UL, Emberstone, 10U, PrizePlacement.Bag)),
            $"{step}: the award, ten Emberstones in the bag and no experience at the level cap of 25");
        Assert.That(
            party.Where(client => client != mvp).Select(client => awards[client].Count),
            Is.All.EqualTo(0),
            $"{step}: the others heard no award");
        kael.World.ItemDroppedReceived -= drops.Add;
        Assert.That(drops.Select(drop => drop.ItemId), Has.Member(MoltenCard), $"{step}: its card dropped");
        foreach (SocketClient client in party)
        {
            client.AutoAttack.OnWalkRequested();
        }

        IReadOnlyList<MonsterDefinitionId> returned = admin
            .RespawnBossesAsync(new AdminActor("acceptance", "test"))
            .GetAwaiter()
            .GetResult();
        Assert.That(returned, Is.EqualTo(new[] { new MonsterDefinitionId(FurnaceHeart) }), $"{step}: brought back");
        Assert.That(
            SocketClients.PumpUntil(
                () => party.All(client => heard[client].Contains($"Appeared {FurnaceHeart}")),
                everyone),
            Is.True,
            $"{step}: every member heard the boss appear");
        SocketClients.PumpFor(TimeSpan.FromSeconds(0.5), everyone);
        Assert.That(
            (heard[ivor].Count, awards[ivor].Count),
            Is.EqualTo((0, 0)),
            $"{step}: Ivor, in town, heard nothing of the quarry's boss");
        return names[mvp];
    }

    // The party walks out of the quarry by its south gate, across the field, and through the field's west gate into
    // town, where each member turns both quests in at the Gate Warden: each is completed, and its coins come with it.
    private static void TurnInTheQuarryQuests(ServerContent content, params SocketClient[] party)
    {
        const string step = "the turn-in";
        CrossTo(step, content, AshenQuarry, TrainingField, party);
        CrossTo(step, content, TrainingField, TrainingGround, party);
        foreach (SocketClient client in party)
        {
            EntityId warden = WalkUpTo(step, GateWarden, client, party);
            foreach (string id in new[] { QuarryHunt, BossQuest })
            {
                QuestDefinition quest = content.Quests[new QuestDefinitionId(id)];
                uint coins = client.World.Inventory.Coins;
                client.Connection.SendCompleteQuest(warden, quest.Id);
                Assert.That(
                    SocketClients.PumpUntil(
                        () => QuestOf(client.World, id) == $"Completed {quest.Count}/{quest.Count}"
                            && client.World.Inventory.Coins == coins + (uint)quest.Currency,
                        party),
                    Is.True,
                    $"{step}: {id} completed and paid; {client.World.LastRejection}");
            }
        }
    }

    // The party walks into the portal from one map toward the next, and each follows its character there.
    private static void CrossTo(string step, ServerContent content, string from, string to, params SocketClient[] party)
    {
        var destination = new MapDefinitionId(to);
        MapPortal portal = content.Maps[new MapDefinitionId(from)].Portals
            .Single(candidate => candidate.DestinationMap == destination);
        Cross(step, portal.Center, destination, party);
    }

    private static InventoryEntry RowOf(SocketClient client, string item)
    {
        return client.World.Inventory.Rows.First(row => row.Item.Value == item);
    }

    private static void Wear(string step, SocketClient client, string item, EquipmentSlot slot,
        params SocketClient[] all)
    {
        long row = RowOf(client, item).InventoryItem;
        Assert.That(client.Connection.SendEquip(row, slot), Is.Not.Zero, $"{step}: {item} worn");
        Assert.That(
            SocketClients.PumpUntil(
                () => client.World.Inventory.Rows.Any(entry => entry.InventoryItem == row && entry.Slot == slot),
                all),
            Is.True,
            $"{step}: {item} in {slot} ({client.World.LastRejection})");
    }

    private static void TakeOff(string step, SocketClient client, EquipmentSlot slot, params SocketClient[] all)
    {
        long row = client.World.Inventory.Rows.Single(entry => entry.Slot == slot).InventoryItem;
        Assert.That(client.Connection.SendUnequip(slot), Is.Not.Zero, $"{step}: {slot} taken off");
        Assert.That(
            SocketClients.PumpUntil(
                () => client.World.Inventory.Rows.Any(entry => entry.InventoryItem == row
                    && entry.Slot == EquipmentSlot.None),
                all),
            Is.True,
            $"{step}: {slot} in the bag ({client.World.LastRejection})");
    }

    private static void Insert(string step, SocketClient client, string card, string item, params SocketClient[] all)
    {
        long cardRow = RowOf(client, card).InventoryItem;
        long itemRow = RowOf(client, item).InventoryItem;
        Assert.That(client.Connection.SendInsertCard(cardRow, itemRow), Is.Not.Zero, $"{step}: {card} sent");
        Assert.That(
            SocketClients.PumpUntil(
                () => client.World.Inventory.Rows.Any(row => row.InventoryItem == itemRow
                    && row.Cards.Any(held => held.Value == card)),
                all),
            Is.True,
            $"{step}: {item} holds {card} ({client.World.LastRejection})");
    }

    // Kael puts the Furnace Heart Card into his leather armor and the Cinder Crawler Card into his cape, and wears
    // both again: his sheet's armor turns Fire, and Tarn's inspection shows both cards; at the Smith, whose fees came in
    // two parts, Kael takes the Furnace Heart Card out for its 5,000 coins (Gameplay Systems §11.7).
    private static void CardTheGearAndTakeOneOut(SocketClient kael, SocketClient tarn, params SocketClient[] all)
    {
        const string step = "the cards";
        WalkTogether(step, BackInTown, all);
        foreach ((string card, string item, EquipmentSlot slot) in new[]
                 {
                     (MoltenCard, "item.armor.leather", EquipmentSlot.Armor),
                     (FirewardCard, "item.garment.gloom_cape", EquipmentSlot.Garment)
                 })
        {
            TakeOff(step, kael, slot, all);
            Insert(step, kael, card, item, all);
            Wear(step, kael, item, slot, all);
        }

        Assert.That(
            SocketClients.PumpUntil(() => kael.World.Sheet?.ArmorElement == Element.Fire, all),
            Is.True,
            $"{step}: Kael's armor is Fire");
        var seen = new List<InspectedEquipment>();
        tarn.Connection.InspectedEquipmentReceived += seen.Add;
        Assert.That(tarn.Connection.SendInspectPlayer(kael.World.LocalEntity), Is.Not.Zero, $"{step}: asked");
        Assert.That(SocketClients.PumpUntil(() => seen.Count > 0, all), Is.True, $"{step}: answered");
        tarn.Connection.InspectedEquipmentReceived -= seen.Add;
        InspectedEquipment answer = seen.Last();
        Assert.That(
            (answer.ItemIn(EquipmentSlot.Armor).Cards.Single().Value,
                answer.ItemIn(EquipmentSlot.Garment).Cards.Single().Value),
            Is.EqualTo((MoltenCard, FirewardCard)),
            $"{step}: Tarn sees both cards");

        EntityId smith = WalkUpTo(step, Smith, kael, all);
        Assert.That(
            SocketClients.PumpUntil(() => kael.World.CardRemovalFeesOf(smith).Count == 17, all),
            Is.True,
            $"{step}: the Smith's seventeen fees");
        TakeOff(step, kael, EquipmentSlot.Armor, all);
        long armor = RowOf(kael, "item.armor.leather").InventoryItem;
        uint coins = kael.World.Inventory.Coins;
        Assert.That(
            kael.Connection.SendRemoveCard(smith, armor, 1, new ItemDefinitionId(MoltenCard)),
            Is.Not.Zero,
            $"{step}: the removal sent");
        Assert.That(
            SocketClients.PumpUntil(() => kael.World.Inventory.Rows.Any(row => row.Item.Value == MoltenCard), all),
            Is.True,
            $"{step}: the card back in the bag ({kael.World.LastRejection})");
        Assert.That(kael.World.Inventory.Coins, Is.EqualTo(coins - 5000), $"{step}: its fee");
        Wear(step, kael, "item.armor.leather", EquipmentSlot.Armor, all);
    }

    // Kael hands the Furnace Heart Card to Tarn in a trade; Tarn stores it with the Storekeeper; Kael lists the Basalt
    // Crawler Card on the board, where Ilsa finds it among thirty-six goods in three messages (Gameplay Systems §11.4,
    // §11.5, §16).
    private static void TradeStoreAndList(SocketClient kael, SocketClient tarn, SocketClient ilsa,
        params SocketClient[] all)
    {
        const string step = "trade, storage, and the board";
        var heard = new Dictionary<SocketClient, List<string>>();
        foreach (SocketClient client in new[] { kael, tarn })
        {
            var events = new List<string>();
            heard.Add(client, events);
            client.Connection.TradeEventReceived += tradeEvent => events.Add($"{tradeEvent.Kind} {tradeEvent.Name}");
        }

        Assert.That(
            tarn.Controller.TryMoveTo(tarn.World.Predictor.Position, kael.World.Predictor.Position),
            Is.True,
            $"{step}: a way to Kael");
        Assert.That(SocketClients.PumpUntil(() => !tarn.Controller.HasPath, all), Is.True, $"{step}: beside Kael");
        kael.Connection.SendTradeRequest(TarnName);
        Assert.That(
            SocketClients.PumpUntil(() => heard[tarn].Contains($"Requested {KaelName}"), all),
            Is.True,
            $"{step}: Tarn asked");
        tarn.Connection.SendTradeReply(KaelName, true);
        Assert.That(
            SocketClients.PumpUntil(() => heard[kael].Contains($"Opened {TarnName}"), all),
            Is.True,
            $"{step}: the trade opened");
        kael.Connection.SendTradeOffer(RowOf(kael, MoltenCard).InventoryItem, 1);
        kael.Connection.SendTradeLock();
        tarn.Connection.SendTradeLock();
        SocketClients.PumpFor(TimeSpan.FromMilliseconds(300), all);
        kael.Connection.SendTradeConfirm();
        tarn.Connection.SendTradeConfirm();
        Assert.That(
            SocketClients.PumpUntil(
                () => tarn.World.Inventory.Rows.Any(row => row.Item.Value == MoltenCard)
                    && kael.World.Inventory.Rows.All(row => row.Item.Value != MoltenCard),
                all),
            Is.True,
            $"{step}: the card went to Tarn ({string.Join(", ", heard[kael])}; {kael.World.LastRejection})");

        var changes = new List<StorageChanged>();
        tarn.Connection.StorageChangedReceived += changes.Add;
        EntityId storekeeper = WalkUpTo(step, Storekeeper, tarn, all);
        tarn.Connection.SendStorageDeposit(storekeeper, RowOf(tarn, MoltenCard).InventoryItem, 1);
        Assert.That(
            SocketClients.PumpUntil(() => changes.Count == 1, all),
            Is.True,
            $"{step}: Tarn stored the card ({tarn.World.LastRejection})");
        tarn.Connection.StorageChangedReceived -= changes.Add;
        Assert.That(
            (changes[0].Row.Item.Value, changes[0].Row.Quantity),
            Is.EqualTo((MoltenCard, 1u)),
            $"{step}: the card in storage");

        var notices = new List<MarketEvent>();
        var boards = new List<MarketBoard>();
        kael.Connection.MarketEventReceived += notices.Add;
        ilsa.Connection.MarketBoardReceived += boards.Add;
        EntityId broker = WalkUpTo(step, Broker, kael, all);
        WalkUpTo(step, Broker, ilsa, all);
        Assert.That(
            kael.Connection.SendMarketList(broker, RowOf(kael, SteadyCard).InventoryItem, 1),
            Is.Not.Zero,
            $"{step}: listed");
        Assert.That(
            SocketClients.PumpUntil(() => notices.Count == 1, all),
            Is.True,
            $"{step}: the listing told ({kael.World.LastRejection})");
        Assert.That(ilsa.Connection.SendMarketOpen(broker), Is.Not.Zero, $"{step}: read");
        Assert.That(
            SocketClients.PumpUntil(() => ilsa.World.Market.IsCurrent && boards.Count > 0, all),
            Is.True,
            $"{step}: Ilsa read the board ({ilsa.World.LastRejection})");
        kael.Connection.MarketEventReceived -= notices.Add;
        ilsa.Connection.MarketBoardReceived -= boards.Add;
        Assert.That(
            boards.SelectMany(board => board.Entries).Single(entry => entry.Item.Value == SteadyCard).Stock,
            Is.EqualTo(1u),
            $"{step}: the card on the board for Ilsa");
        Assert.That(
            (boards.Count, boards.Sum(board => board.Entries.Count)),
            Is.EqualTo((3, 36)),
            $"{step}: thirty-six goods in three messages");
    }

    // The test package (Coding Standards §10): the quarry's monsters keep their spawns but swing for 1 and never dodge,
    // the crawlers have 100 HP, the wisps 300, and the boss 60; the Ash Wisp casts Hush whenever it may and the boss
    // Furnace Burst, and no other monster skill is cast; the Quarry Hunt asks for one crawler.
    private static Action<Dictionary<string, byte[]>>[] Weakened()
    {
        var edits = new List<Action<Dictionary<string, byte[]>>>
        {
            DungeonPackage.NeverCast(CinderCrawler, 0), DungeonPackage.NeverCast(BasaltCrawler, 0),
            DungeonPackage.NeverCast(AshWisp, 0), DungeonPackage.AlwaysCast(AshWisp),
            DungeonPackage.NeverCast(FurnaceHeart, 1), DungeonPackage.AlwaysCast(FurnaceHeart),
            DungeonPackage.Weaken(FurnaceHeart, 60), DungeonPackage.Muffle(FurnaceHeart),
            DungeonPackage.Muffle(AshWisp),
            DungeonPackage.Shorten(QuarryHunt, 1)
        };
        foreach (string monster in new[] { CinderCrawler, BasaltCrawler, AshWisp, FurnaceHeart })
        {
            edits.Add(DungeonPackage.Disarm(monster));
            edits.Add(DungeonPackage.Expose(monster));
        }

        foreach (string monster in new[] { CinderCrawler, BasaltCrawler })
        {
            edits.Add(DungeonPackage.Weaken(monster, 100));
        }

        // The wisp outlives the hit that provokes it.
        edits.Add(DungeonPackage.Weaken(AshWisp, 300));

        return edits.ToArray();
    }

    private static string[] Bag(SocketClient client)
    {
        return client.World.Inventory.Rows
            .Select(row => $"{row.Item.Value} x {row.Quantity}"
                + (row.Cards.Count == 0
                    ? string.Empty
                    : $" [{string.Join(", ", row.Cards.Select(card => card.Value))}]")
                + (row.Slot == EquipmentSlot.None ? string.Empty : $" in {row.Slot}"))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToArray();
    }

    // Every hit, dodge roll, and ailment lands: dodges and criticals draw 999, everything else 0.
    private sealed class Landing : IRandomSource
    {
        public int Next(int exclusiveMax)
        {
            return exclusiveMax == 1000 ? 999 : 0;
        }
    }

    [Test]
    public void AParty_OverRealSocketsAndPostgres_HuntsTheQuarry_FellsItsBoss_AndKeepsWhatItEarnedThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(Path.Combine(root.Path, "content", "server"), DungeonPackage.Build(true, Weakened()));

        ServerContent content;
        string mvpName;
        uint kaelCoins;
        string[] kaelBag;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
            using var kael = new SocketClient(content, KaelIdentity, KaelName);
            using var tarn = new SocketClient(content, TarnIdentity, TarnName);
            using var ilsa = new SocketClient(content, IlsaIdentity, IlsaName);
            SocketClient[] party = { kael, tarn, ilsa };
            EnterTogether(port, kael, tarn, ilsa);
            AssertCleanTraffic("enter", party);

            VisitTheSmithAndTheGateWarden(content, kael, party);
            AssertCleanTraffic("in town", party);

            FormParty(kael, tarn, ilsa);
            AcceptTheQuarryQuests(party);
            AssertCleanTraffic("the quests", party);

            CrossToTheField(content, party);
            WalkUnderTheNorthWall(party);
            CrossToTheQuarry(content, party);
            AssertCleanTraffic("into the quarry", party);

            HuntACinderCrawler(kael, party);
            AssertCleanTraffic("the hunt", party);

            MeetTheBasaltPair(kael, party);
            AssertCleanTraffic("the pair", party);

            MeetTheAshWisp(ilsa, party);
            AssertCleanTraffic("the wisp", party);

            // Ivor enters only now, in town, so the clients waiting on the others keep their connections fresh.
            using var ivor = new SocketClient(content, IvorIdentity, IvorName);
            ivor.EnterWorld(port);
            mvpName = MeetTheFurnaceHeart(admin, ivor, kael, tarn, ilsa);
            AssertCleanTraffic("the boss", kael, tarn, ilsa, ivor);
            ivor.Disconnect();

            TurnInTheQuarryQuests(content, party);
            AssertCleanTraffic("the turn-in", party);

            CardTheGearAndTakeOneOut(kael, tarn, party);
            AssertCleanTraffic("the cards", party);

            TradeStoreAndList(kael, tarn, ilsa, party);
            AssertCleanTraffic("trade, storage, and the board", party);
            kaelCoins = kael.World.Inventory.Coins;
            kaelBag = Bag(kael);
            Stop(host, party);
        }

        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var kael = new SocketClient(content, KaelIdentity, KaelName);
            using var tarn = new SocketClient(content, TarnIdentity, TarnName);
            using var ilsa = new SocketClient(content, IlsaIdentity, IlsaName);
            SocketClient[] party = { kael, tarn, ilsa };
            const string step = "restart";
            foreach (SocketClient client in party)
            {
                client.EnterWorld(port);
                Assert.That(
                    client.World.Map,
                    Is.EqualTo(new MapDefinitionId(TrainingGround)),
                    $"{step}: in town");
                Assert.That(
                    new[] { QuarryHunt, BossQuest }.Select(quest => QuestOf(client.World, quest)),
                    Is.All.EqualTo("Completed 1/1"),
                    $"{step}: both quests completed");
            }

            AssertBuilt(step, kael, Vanguard, VanguardBuild, VanguardStats, 0, "item.weapon.iron_sword");
            AssertBuilt(step, tarn, Vanguard, VanguardBuild, VanguardStats, 0, "item.weapon.iron_sword");
            AssertBuilt(step, ilsa, Arcanist, ArcanistBuild, ArcanistStats, 1, "item.weapon.ash_staff");
            Assert.That((kael.World.Inventory.Coins, Bag(kael)), Is.EqualTo((kaelCoins, kaelBag)), $"{step}: Kael's");
            Assert.That(
                Bag(kael),
                Has.Member($"item.garment.gloom_cape x 1 [{FirewardCard}] in Garment")
                    .And.No.Member($"{MoltenCard} x 1")
                    .And.No.Member($"{SteadyCard} x 1"),
                $"{step}: the Fireward cape worn, the Molten and Steady cards gone");
            SocketClient mvp = party.Single(client =>
                client.Connection.Characters.Any(character => character.Name == mvpName));
            Assert.That(
                mvp.World.Inventory.Rows.Where(row => row.Item.Value == Emberstone).Sum(row => row.Quantity),
                Is.GreaterThanOrEqualTo(10u),
                $"{step}: {mvpName}'s ten Emberstones");

            var parts = new List<StorageSnapshot>();
            tarn.Connection.StorageSnapshotReceived += parts.Add;
            EntityId storekeeper = WalkUpTo(step, Storekeeper, tarn, party);
            tarn.Connection.SendStorageOpen(storekeeper);
            Assert.That(SocketClients.PumpUntil(() => parts.Count > 0, party), Is.True, $"{step}: Tarn read storage");
            Assert.That(
                parts.SelectMany(part => part.Entries).Select(entry => (entry.Item.Value, entry.Quantity)),
                Is.EqualTo(new[] { (MoltenCard, 1u) }),
                $"{step}: the Molten card in Tarn's storage, once");
            Assert.That(
                tarn.World.Inventory.Rows.Any(row => row.Item.Value == MoltenCard),
                Is.False,
                $"{step}: and not in his bag");

            var boards = new List<MarketBoard>();
            ilsa.Connection.MarketBoardReceived += boards.Add;
            EntityId broker = WalkUpTo(step, Broker, ilsa, party);
            ilsa.Connection.SendMarketOpen(broker);
            Assert.That(
                SocketClients.PumpUntil(() => ilsa.World.Market.IsCurrent && boards.Count > 0, party),
                Is.True,
                $"{step}: Ilsa read the board");
            Assert.That(
                boards.SelectMany(board => board.Entries).Single(entry => entry.Item.Value == SteadyCard).Stock,
                Is.EqualTo(1u),
                $"{step}: the Steady card still listed, once");
            AssertCleanTraffic(step, party);
            Stop(host, party);
        }
    }
}
}
