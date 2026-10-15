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
///     training field wearing a training sword and holding cloth armor, a Grotto Crawler Card, and an Earth Converter,
///     and Arlo, of another account, stored beside her as an Arcanist wearing the training staff and cloth armor carded
///     with a Grotto Crawler Card, Earthen, with Arcane Bolt learned, enter and see each other by name, his sheet's armor
///     Earth. Vera's sword fells a Spark Wisp in three hits for the sword's least
///     draw against its small size; she uses the converter, her sheet and status list say Earth, and her sword fells
///     another wisp, Wind, in two; and Arlo's bolt strikes a third. Then Vera leaves, and Arlo sees her go. After a
///     clean stop a second host on the same database lets both in again on the field, their bags as they left them, the
///     converter's element kept, since every status is saved (Milestone 19), and Arlo's armor Earth still. In the Umbral
///     Grotto, with a test package that disarms
///     its monsters, has the Barrow Crawler bite and the Slime Monarch slam whenever they may, and softens the slam,
///     Wren's Neutral sword strikes a Hollow Wisp, a Ghost, for half, and a Barrow Crawler bites Ilse, an Arcanist in
///     Earthen armor, with Grave Bite; after a restart that finds them both beside the Monarch, its Earth slam strikes
///     Ilse's Earth armor for a quarter of what it strikes Wren for.
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
    private const string EarthConverter = "item.consumable.earth_converter";
    private const string EarthWeapon = "status.earth_weapon";
    private const string Arcanist = "job.arcanist";
    private const string VeraIdentity = "element-vera";
    private const string VeraName = "Vera";
    private const string ArloIdentity = "element-arlo";
    private const string ArloName = "Arlo";
    private const string WrenIdentity = "element-wren";
    private const string WrenName = "Wren";
    private const string IlseIdentity = "element-ilse";
    private const string IlseName = "Ilse";
    private const string UmbralGrotto = "map.umbral_grotto";
    private const string HollowWisp = "monster.hollow_wisp";
    private const string BarrowCrawler = "monster.barrow_crawler";
    private const string SlimeMonarch = "monster.slime_monarch";

    // The training sword's least draw, 19, three quarters of it against the Small wisp, 14, and twice the status
    // attack of 7, less the wisp's 1 defense (Gameplay Systems §11.1).
    private const uint SwordOnAWisp = 27;

    // The sized draw, 14, half again for Earth on Wind, 21, beside twice the status attack of 8, since the first
    // wisp's experience lifted Vera to level 2, less the wisp's 1 defense.
    private const uint EarthSwordOnAWisp = 36;

    // Half of each Neutral part on the Ghost: twice half the status attack of 7, 6, beside half the sized draw of 14,
    // 7, less the Hollow Wisp's 8 defense (Gameplay Systems §3, §11.1).
    private const uint SwordOnAGhost = 12;

    // The test package's slam: a magic attack of 40, drawn at its least, 32, by 120 %, 38, before each player's magic
    // defense (Gameplay Systems §9).
    private const int SoftSlam = 40;

    private static readonly SkillDefinitionId ArcaneBolt = new("skill.arcane_bolt");
    private static readonly SkillDefinitionId GraveBite = new("skill.grave_bite");
    private static readonly SkillDefinitionId QuakeSlam = new("skill.quake_slam");
    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CastLimit = TimeSpan.FromSeconds(4);

    // West of the field's three Spark Wisps, beyond what the aggressive crawlers in its north notice.
    private static readonly WorldPosition VeraStands = new(0.5f, 0f, -10.5f);
    private static readonly WorldPosition ArloStands = new(0.5f, 0f, -8.5f);

    // Inside the Hollow Wisps' spawn in the wisp gallery's south-east corner, and inside the Barrow Crawlers' in the
    // entrance hall's south; then 2.5 m and 2.8 m from the Slime Monarch's home at (18, 0, 18), within its slam's 4 m.
    private static readonly WorldPosition WrenByTheGhosts = new(22f, 0f, -26.5f);
    private static readonly WorldPosition IlseByThePack = new(-20.5f, 0f, -24f);
    private static readonly WorldPosition WrenBeforeTheBoss = new(15.5f, 0f, 18f);
    private static readonly WorldPosition IlseBesideTheBoss = new(16f, 0f, 20f);

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
            StoreItem(VeraName, EarthConverter);
        };
        arlo.AfterCreate = () =>
        {
            Place(ArloName, ArloStands);
            new BuildSeed(1, new Dictionary<string, int> { [ArcaneBolt.Value] = 1 }, Arcanist)
                .Apply(m_database.ConnectionString, ArloName);
            StoreWorn(ArloName, TrainingStaff);
            StoreWornArmor(ArloName, ClothArmor, GrottoCrawlerCard);
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
            Is.EqualTo(
                new[]
                {
                    $"{ClothArmor} x 1", $"{GrottoCrawlerCard} x 1", $"{EarthConverter} x 1",
                    $"{TrainingSword} x 1 worn"
                }),
            $"{step}: Vera's bag as stored");
        Assert.That(
            Bag(arlo),
            Is.EqualTo(new[] { $"{ClothArmor} x 1 [{GrottoCrawlerCard}] worn", $"{TrainingStaff} x 1 worn" }),
            $"{step}: Arlo's bag as stored");
        Assert.That(
            SocketClients.PumpUntil(() => arlo.World.Sheet?.ArmorElement == Element.Earth, both),
            Is.True,
            $"{step}: Arlo's Earthen armor is Earth");
    }

    // Wren, with a training sword, and Ilse, an Arcanist in Earthen cloth armor, stored in the grotto: Wren by the
    // Hollow Wisps, Ilse by the Barrow Crawlers.
    private void EnterTheGrotto(int port, SocketClient wren, SocketClient ilse)
    {
        const string step = "into the grotto";
        wren.AfterCreate = () =>
        {
            Place(WrenName, WrenByTheGhosts, UmbralGrotto);
            StoreWorn(WrenName, TrainingSword);
        };
        ilse.AfterCreate = () =>
        {
            Place(IlseName, IlseByThePack, UmbralGrotto);
            new BuildSeed(1, new Dictionary<string, int> { [ArcaneBolt.Value] = 1 }, Arcanist)
                .Apply(m_database.ConnectionString, IlseName);
            StoreWorn(IlseName, TrainingStaff);
            StoreWornArmor(IlseName, ClothArmor, GrottoCrawlerCard);
        };
        wren.EnterWorld(port);
        ilse.EnterWorld(port);
        foreach (SocketClient client in new[] { wren, ilse })
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(UmbralGrotto)), $"{step}: in the grotto");
        }
    }

    // Wren's swing at a Hollow Wisp lands for half of each Neutral part; Wren then stops (Gameplay Systems §3).
    private static void SwingAtTheGhost(SocketClient wren, SocketClient ilse)
    {
        const string step = "the ghost";
        ClientWorld world = wren.World;
        EntityId ghost = TargetA(step, HollowWisp, wren, ilse);
        var hits = new List<uint>();

        void OnDamage(Damage damage)
        {
            if (damage.Source == world.LocalEntity && damage.Target == ghost)
            {
                hits.Add(damage.Amount);
            }
        }

        world.DamageReceived += OnDamage;
        wren.AttackTarget();
        Assert.That(SocketClients.PumpUntil(() => hits.Count > 0, FightLimit, wren, ilse), Is.True, $"{step}: a hit");
        world.DamageReceived -= OnDamage;
        wren.AutoAttack.OnWalkRequested();
        Assert.That(hits[0], Is.EqualTo(SwordOnAGhost), $"{step}: half of each Neutral part");
    }

    // Ilse strikes a Barrow Crawler, which bites back with Grave Bite, a skill with no cast time; the package's
    // disarmed bite strikes for the least, 1 (Gameplay Systems §9).
    private static void BittenByThePack(SocketClient ilse, SocketClient wren)
    {
        const string step = "the bite";
        ClientWorld world = ilse.World;
        TargetA(step, BarrowCrawler, ilse, wren);
        var resolved = new List<SkillResolved>();
        world.SkillResolvedReceived += resolved.Add;

        ilse.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(
                () => resolved.Any(skill => skill.Skill == GraveBite && skill.Target == world.LocalEntity),
                FightLimit,
                ilse,
                wren),
            Is.True,
            $"{step}: bitten");
        world.SkillResolvedReceived -= resolved.Add;
        ilse.AutoAttack.OnWalkRequested();

        SkillResolved bite = resolved.First(skill => skill.Skill == GraveBite && skill.Target == world.LocalEntity);
        Assert.That(world.Remotes[bite.Caster].DefinitionId, Is.EqualTo(BarrowCrawler), $"{step}: by a crawler");
        Assert.That((bite.Outcome, bite.Amount), Is.EqualTo((SkillOutcome.Hit, 1u)), $"{step}: for 1");
    }

    // The Slime Monarch slams the two beside it: Earth on Wren's Neutral armor whole, on Ilse's Earth armor a quarter,
    // each after its own magic defense, which the sheets give (Gameplay Systems §3, §9).
    private static void SlammedOnEarth(SocketClient wren, SocketClient ilse)
    {
        const string step = "the slam";
        SkillResolved? onWren = null;
        SkillResolved? onIlse = null;
        wren.World.SkillResolvedReceived += resolved =>
            onWren ??= resolved.Skill == QuakeSlam && resolved.Target == wren.World.LocalEntity ? resolved : null;
        ilse.World.SkillResolvedReceived += resolved =>
            onIlse ??= resolved.Skill == QuakeSlam && resolved.Target == ilse.World.LocalEntity ? resolved : null;

        Assert.That(
            SocketClients.PumpUntil(() => onWren != null && onIlse != null, FightLimit, wren, ilse),
            Is.True,
            $"{step}: both slammed");
        uint slam = onWren!.Amount + wren.World.Sheet!.MagicDefense;
        Assert.That((onWren.Outcome, slam), Is.EqualTo((SkillOutcome.Hit, 38u)), $"{step}: Wren's whole slam");
        Assert.That(
            (onIlse!.Outcome, onIlse.Amount),
            Is.EqualTo((SkillOutcome.Hit, (slam - ilse.World.Sheet!.MagicDefense) * 25 / 100)),
            $"{step}: a quarter on Ilse's Earth armor");
    }

    private static Action<Dictionary<string, byte[]>> MagicAttack(string monster, int value)
    {
        return files => PackageFixture.SetValue(
            files,
            "monsters.json",
            monster,
            "magicAttack",
            value.ToString(CultureInfo.InvariantCulture));
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
        for (int tries = 0; tries < 12 && !IsLive(world, world.Target, monster); tries++)
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

    // Vera attacks the wisp Tab found: the client walks into range, and every scripted hit lands for the same amount
    // until the wisp falls (Gameplay Systems §6, §11.1).
    private static void FellAWisp(string step, SocketClient vera, SocketClient arlo, uint amount, int count)
    {
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
        Assert.That(hits, Has.Length.EqualTo(count).And.All.EqualTo(amount), $"{step}: {count} hits of {amount}");
        Assert.That(world.IsLocalDead, Is.False, $"{step}: Vera survived");
        Assert.That(
            SocketClients.PumpUntil(() => !vera.AutoAttack.IsActive, vera, arlo),
            Is.True,
            $"{step}: the attack ended");
    }

    // Vera uses her Earth Converter: once its commit settles, her sheet's attack element and her status list say Earth
    // (Gameplay Systems §9.1, §11.2).
    private static void Convert(SocketClient vera, SocketClient arlo)
    {
        const string step = "the converter";
        ClientWorld world = vera.World;
        InventoryEntry converter = world.Inventory.Rows.Single(row => row.Item.Value == EarthConverter);

        vera.Connection.SendUseItem(converter.InventoryItem);

        Assert.That(
            SocketClients.PumpUntil(
                () => world.Sheet?.AttackElement == Element.Earth
                    && world.StatusEffects.Any(effect => effect.Status.Value == EarthWeapon),
                vera,
                arlo),
            Is.True,
            $"{step}: Vera's swings are Earth ({world.LastRejection})");
        Assert.That(
            world.Inventory.Rows.Select(row => row.Item.Value),
            Has.No.Member(EarthConverter),
            $"{step}: used up");
    }

    // Arlo targets another wisp and casts Arcane Bolt: the client walks into the bolt's range, and the bolt strikes
    // (Gameplay Systems §9). A wisp that roams out of the bolt's range while it is cast ends the cast unheard (finding
    // R1), so Arlo casts again until one lands.
    private static void BoltAWisp(SocketClient arlo, SocketClient vera)
    {
        const string step = "the bolt";
        ClientWorld world = arlo.World;
        EntityId wisp = TargetA(step, SparkWisp, arlo, vera);
        var resolved = new List<SkillResolved>();
        world.SkillResolvedReceived += resolved.Add;

        bool IsBolted()
        {
            return resolved.Any(skill => skill.Caster == world.LocalEntity && skill.Skill == ArcaneBolt);
        }

        Assert.That(arlo.UseSkill(ArcaneBolt), Is.True, $"{step}: cast");
        for (int casts = 1; casts < 8 && !SocketClients.PumpUntil(IsBolted, CastLimit, arlo, vera); casts++)
        {
            arlo.UseSkill(ArcaneBolt);
        }

        Assert.That(IsBolted(), Is.True, $"{step}: resolved ({world.LastRejection})");
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

    private void Place(string name, WorldPosition position, string map = TrainingField)
    {
        Execute(
            $"UPDATE characters SET map_definition_id = '{map}', position_x = {Number(position.X)}, "
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

    private void StoreWornArmor(string name, string armor, string card)
    {
        Execute(
            "WITH worn AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"version, instance_data_json) SELECT id, '{armor}', 1, 0, 0, '{{\"cards\": [\"{card}\"]}}'::jsonb "
            + $"FROM characters WHERE name = '{name}' RETURNING character_id, id) INSERT INTO equipment "
            + "(character_id, slot, inventory_item_id, version) SELECT character_id, 'Armor', id, 0 FROM worn");
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
    public void TwoPlayers_InTheGrotto_MeetTheGhostAndTheBite_ThenTheSlamOnEarthArmor()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            DungeonPackage.Build(DungeonPackage.WithoutAilmentSkills().Concat(new[]
            {
                DungeonPackage.Disarm(HollowWisp),
                DungeonPackage.Disarm(BarrowCrawler),
                DungeonPackage.AlwaysCast(BarrowCrawler),
                DungeonPackage.Disarm(SlimeMonarch),
                DungeonPackage.AlwaysCast(SlimeMonarch),
                MagicAttack(SlimeMonarch, SoftSlam)
            }).ToArray()));

        ServerContent content;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var wren = new SocketClient(content, WrenIdentity, WrenName);
            using var ilse = new SocketClient(content, IlseIdentity, IlseName);
            EnterTheGrotto(port, wren, ilse);
            AssertCleanTraffic("into the grotto", wren, ilse);

            SwingAtTheGhost(wren, ilse);
            AssertCleanTraffic("the ghost", wren, ilse);
            BittenByThePack(ilse, wren);
            AssertCleanTraffic("the bite", wren, ilse);
            Stop(host, wren, ilse);
        }

        Place(WrenName, WrenBeforeTheBoss, UmbralGrotto);
        Place(IlseName, IlseBesideTheBoss, UmbralGrotto);
        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var wren = new SocketClient(content, WrenIdentity, WrenName);
            using var ilse = new SocketClient(content, IlseIdentity, IlseName);
            wren.EnterWorld(port);
            ilse.EnterWorld(port);
            Assert.That(
                SocketClients.PumpUntil(() => ilse.World.Sheet?.ArmorElement == Element.Earth, wren, ilse),
                Is.True,
                "restart: Ilse's carded armor is Earth");

            SlammedOnEarth(wren, ilse);
            AssertCleanTraffic("the slam", wren, ilse);
            Stop(host, wren, ilse);
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

            FellAWisp("the sword", vera, arlo, SwordOnAWisp, 3);
            AssertCleanTraffic("the sword", vera, arlo);
            Convert(vera, arlo);
            FellAWisp("the converted sword", vera, arlo, EarthSwordOnAWisp, 2);
            AssertCleanTraffic("the converter", vera, arlo);
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
            Assert.That(
                SocketClients.PumpUntil(() => vera.World.Sheet?.AttackElement == Element.Earth, vera, arlo),
                Is.True,
                "restart: every status is saved, so the converter's element stays");
            Assert.That(
                vera.World.StatusEffects.Select(effect => effect.Status.Value),
                Is.EqualTo(new[] { EarthWeapon }),
                "restart: and its status");
            Assert.That(
                SocketClients.PumpUntil(() => arlo.World.Sheet?.ArmorElement == Element.Earth, vera, arlo),
                Is.True,
                "restart: Arlo's carded armor keeps its element");
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
