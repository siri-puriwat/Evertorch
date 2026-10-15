using System;
using System.Collections.Generic;
using System.Globalization;
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
///     The Milestone 19 "Status ailments" exit criterion's server side end to end (ROADMAP §8): the composed server host
///     on a real PostgreSQL 18 and real UDP sockets on loopback, with scripted combat and drops and a grotto test package
///     that disarms the Grotto Crawlers and takes their dodge away, and two clients of two accounts built from the
///     client's production networking and gameplay code. Sora, a level-25 Vanguard with Heavy Blow 5 and Iron Guard 3
///     wearing the Iron Sword, and Ines, a level-25 Arcanist with Arcane Bolt 5 and Mend 3 wearing the Ash Staff, stored
///     in the Umbral Grotto's crawler hall, enter and see each other by name. Sora raises Iron Guard and Ines Mends Sora.
///     Ines's Frost Lance freezes a Grotto Crawler, which both see; Sora's Heavy Blow on it thaws it and stuns nothing,
///     and
///     the next blow stuns it, which both see too, every ailment's draw landing. After a clean stop a second host on the
///     same database lets both in again in the hall, each with its job and its skills, and Sora with its Iron Guard and
///     the time it had left.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class AilmentAcceptanceTests
{
    private const string UmbralGrotto = "map.umbral_grotto";
    private const string GrottoCrawler = "monster.grotto_crawler";
    private const string IronSword = "item.weapon.iron_sword";
    private const string AshStaff = "item.weapon.ash_staff";
    private const string Vanguard = "job.vanguard";
    private const string Arcanist = "job.arcanist";
    private const string IronGuardStatus = "status.iron_guard";
    private const string SoraIdentity = "ailment-sora";
    private const string SoraName = "Sora";
    private const string InesIdentity = "ailment-ines";
    private const string InesName = "Ines";

    private static readonly SkillDefinitionId HeavyBlow = new("skill.heavy_blow");
    private static readonly SkillDefinitionId WarCry = new("skill.war_cry");
    private static readonly SkillDefinitionId IronGuard = new("skill.iron_guard");
    private static readonly SkillDefinitionId ArcaneBolt = new("skill.arcane_bolt");
    private static readonly SkillDefinitionId Clarity = new("skill.clarity");
    private static readonly SkillDefinitionId Mend = new("skill.mend");
    private static readonly SkillDefinitionId FrostLance = new("skill.frost_lance");
    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);

    // Two places on the crawler hall's floor, at its south end, far from the passive pack around (-18.5, 0, 16.5).
    private static readonly WorldPosition SoraStands = new(-22f, 0f, 4.5f);
    private static readonly WorldPosition InesStands = new(-20.5f, 0f, 4.5f);

    // Job level 10 grants a first job its base job's nine points and nine more, so each build below is within them
    // and the load keeps it (Gameplay Systems §2.1).
    private static readonly BuildSeed SoraBuild = new(
        10,
        new Dictionary<string, int> { [HeavyBlow.Value] = 5, [WarCry.Value] = 1, [IronGuard.Value] = 3 },
        Vanguard);

    private static readonly BuildSeed InesBuild = new(
        10,
        new Dictionary<string, int>
        {
            [ArcaneBolt.Value] = 5, [Clarity.Value] = 1, [Mend.Value] = 3, [FrostLance.Value] = 5
        },
        Arcanist);

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
    // draws, every ailment's roll lands, and nothing drops, since network timing decides which draw each roll gets
    // (Coding Standards §10).
    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=19",
                "--Session:ReconnectGraceMs=0"
            },
            contentRootPath,
            m_database.ConnectionString);
        TestHosts.ScriptOutcomes(builder, new LandingRandom(), new ScriptedRandom(999_999));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private void EnterTheHall(int port, SocketClient sora, SocketClient ines)
    {
        const string step = "into the grotto";
        sora.AfterCreate = () =>
        {
            Place(SoraName, SoraStands);
            SoraBuild.Apply(m_database.ConnectionString, SoraName);
            Execute($"UPDATE characters SET base_level = 25, hp = 100000, sp = 100000 WHERE name = '{SoraName}'");
            StoreWorn(SoraName, IronSword);
        };
        ines.AfterCreate = () =>
        {
            Place(InesName, InesStands);
            InesBuild.Apply(m_database.ConnectionString, InesName);
            Execute($"UPDATE characters SET base_level = 25, hp = 100000, sp = 100000 WHERE name = '{InesName}'");
            StoreWorn(InesName, AshStaff);
        };
        sora.EnterWorld(port);
        ines.EnterWorld(port);
        foreach (SocketClient client in new[] { sora, ines })
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(UmbralGrotto)), $"{step}: in the grotto");
        }

        Assert.That(Bag(sora), Is.EqualTo(new[] { $"{IronSword} x 1 worn" }), $"{step}: Sora's sword, worn");
        Assert.That(Bag(ines), Is.EqualTo(new[] { $"{AshStaff} x 1 worn" }), $"{step}: Ines's staff, worn");
        AssertBuild(step, sora, Vanguard, SoraBuild);
        AssertBuild(step, ines, Arcanist, InesBuild);
    }

    // The sheet names the stored job and the skill list each stored skill at its level (Network Protocol §9).
    private static void AssertBuild(string step, SocketClient client, string job, BuildSeed build)
    {
        ClientWorld world = client.World;
        Assert.That(
            SocketClients.PumpUntil(() => world.Sheet != null && world.Skills.Count > 0, client),
            Is.True,
            $"{step}: the sheet and the skill list");
        Assert.That(world.Sheet!.Job, Is.EqualTo(new JobDefinitionId(job)), $"{step}: {world.Sheet.Job}");
        Assert.That(
            world.Skills.Where(entry => entry.Level > 0).Select(entry => (entry.Skill.Value, (int)entry.Level)),
            Is.EquivalentTo(build.Skills.Select(skill => (skill.Key, skill.Value))),
            $"{step}: the stored skills");
    }

    // Sora raises Iron Guard on itself; its status list then holds the effect (Gameplay Systems §9.1).
    private static void RaiseIronGuard(SocketClient sora, SocketClient ines)
    {
        const string step = "Iron Guard";
        ClientWorld world = sora.World;
        Assert.That(sora.UseSkill(IronGuard), Is.True, $"{step}: cast");
        Assert.That(
            SocketClients.PumpUntil(
                () => world.StatusEffects.Any(effect => effect.Status.Value == IronGuardStatus),
                sora,
                ines),
            Is.True,
            $"{step}: in Sora's status list ({world.LastRejection})");
    }

    // Every status is saved with its time left, which waited while the server was down (Gameplay Systems §9.1).
    private static void AssertIronGuardKept(SocketClient sora, SocketClient ines)
    {
        const string step = "restart";
        ClientWorld world = sora.World;
        Assert.That(
            SocketClients.PumpUntil(
                () => world.StatusEffects.Any(effect => effect.Status.Value == IronGuardStatus),
                sora,
                ines),
            Is.True,
            $"{step}: Sora's Iron Guard kept");
        uint left = world.StatusEffects.Single(effect => effect.Status.Value == IronGuardStatus).RemainingMs;
        Assert.That(left, Is.InRange(1u, 60_000u), $"{step}: with what it had left");
    }

    // Ines heals Sora with Mend: the heal names Sora and its level's amount (Gameplay Systems §9).
    private static void MendSora(SocketClient ines, SocketClient sora)
    {
        const string step = "Mend";
        ClientWorld world = ines.World;
        EntityId target = sora.World.LocalEntity;
        var resolved = new List<SkillResolved>();
        world.SkillResolvedReceived += resolved.Add;

        ines.Connection.SendUseSkill(Mend, target);
        Assert.That(
            SocketClients.PumpUntil(
                () => resolved.Any(skill => skill.Caster == world.LocalEntity && skill.Skill == Mend),
                FightLimit,
                ines,
                sora),
            Is.True,
            $"{step}: resolved ({world.LastRejection})");
        world.SkillResolvedReceived -= resolved.Add;

        SkillResolved heal = resolved.First(skill => skill.Caster == world.LocalEntity && skill.Skill == Mend);
        Assert.That((heal.Target, heal.Outcome), Is.EqualTo((target, SkillOutcome.Healed)), $"{step}: on Sora");
        Assert.That(heal.Amount, Is.EqualTo(100u), $"{step}: Mend 3's 100 HP");
    }

    // Ines selects a Grotto Crawler and casts Frost Lance: it strikes, and its freeze lands, which both clients see in
    // the crawler's flags (Gameplay Systems §9.1).
    private static EntityId FrostLanceOnACrawler(SocketClient ines, SocketClient sora)
    {
        const string step = "Frost Lance";
        ClientWorld world = ines.World;
        EntityId crawler = TargetA(step, GrottoCrawler, ines, sora);
        var resolved = new List<SkillResolved>();
        world.SkillResolvedReceived += resolved.Add;

        Assert.That(ines.UseSkill(FrostLance), Is.True, $"{step}: cast");
        Assert.That(
            SocketClients.PumpUntil(
                () => resolved.Any(skill => skill.Caster == world.LocalEntity && skill.Skill == FrostLance),
                FightLimit,
                ines,
                sora),
            Is.True,
            $"{step}: resolved ({world.LastRejection})");
        world.SkillResolvedReceived -= resolved.Add;

        SkillResolved lance = resolved.First(skill => skill.Caster == world.LocalEntity && skill.Skill == FrostLance);
        Assert.That((lance.Target, lance.Outcome), Is.EqualTo((crawler, SkillOutcome.Hit)), $"{step}: on the crawler");
        AssertFlag(step, crawler, EntityStateFlags.Frozen, true, ines, sora);
        return crawler;
    }

    // Both clients see the flag on the entity as the server set it (Network Protocol §6).
    private static void AssertFlag(
        string step,
        EntityId entity,
        EntityStateFlags flag,
        bool isSet,
        params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            Assert.That(
                SocketClients.PumpUntil(
                    () => client.World.Remotes.TryGetValue(entity, out RemoteEntity? remote)
                        && (remote.StateFlags & flag) != 0 == isSet,
                    clients),
                Is.True,
                $"{step}: {flag} {(isSet ? "seen" : "gone")}");
        }
    }

    // Sora's first Heavy Blow on the frozen crawler thaws it and, the hit having broken a hold, stuns nothing; its next
    // blow stuns it (Gameplay Systems §9.1).
    private static void HeavyBlowsOn(EntityId crawler, SocketClient sora, SocketClient ines)
    {
        const string step = "Heavy Blow";
        ClientWorld world = sora.World;
        for (int tries = 0; tries < 8 && world.Target != crawler; tries++)
        {
            EntityId next = sora.CycleTarget(true);
            SocketClients.PumpUntil(() => world.Target == next, sora, ines);
        }

        Assert.That(world.Target, Is.EqualTo(crawler), $"{step}: Sora selects Ines's crawler");
        Blow(step, crawler, sora, ines);
        AssertFlag(step, crawler, EntityStateFlags.Frozen, false, sora, ines);
        Assert.That(
            world.Remotes[crawler].StateFlags & EntityStateFlags.Stunned,
            Is.EqualTo(EntityStateFlags.None),
            $"{step}: the breaking blow stuns nothing");
        Assert.That(
            SocketClients.PumpUntil(
                () => world.CooldownRemaining(HeavyBlow) <= 0.0 && world.AfterCastDelayRemaining <= 0.0,
                FightLimit,
                sora,
                ines),
            Is.True,
            $"{step}: cooled down");
        Blow($"{step} again", crawler, sora, ines);
        AssertFlag($"{step} again", crawler, EntityStateFlags.Stunned, true, sora, ines);
    }

    // Sora uses Heavy Blow on its selected crawler: the client walks into its reach, and the blow strikes, since every
    // hit lands (Gameplay Systems §9).
    private static void Blow(string step, EntityId crawler, SocketClient sora, SocketClient ines)
    {
        ClientWorld world = sora.World;
        var resolved = new List<SkillResolved>();
        world.SkillResolvedReceived += resolved.Add;

        Assert.That(sora.UseSkill(HeavyBlow), Is.True, $"{step}: cast");
        Assert.That(
            SocketClients.PumpUntil(
                () => resolved.Any(skill => skill.Caster == world.LocalEntity && skill.Skill == HeavyBlow),
                FightLimit,
                sora,
                ines),
            Is.True,
            $"{step}: resolved ({world.LastRejection})");
        world.SkillResolvedReceived -= resolved.Add;

        SkillResolved blow = resolved.First(skill => skill.Caster == world.LocalEntity && skill.Skill == HeavyBlow);
        Assert.That((blow.Target, blow.Outcome), Is.EqualTo((crawler, SkillOutcome.Hit)), $"{step}: on the crawler");
        Assert.That(blow.Amount, Is.GreaterThan(0u), $"{step}: some damage");
    }

    // Every hit lands at the least of its draws, as SureHitRandom's, and every ailment's roll draws 0, so lands.
    private sealed class LandingRandom : IRandomSource
    {
        public int Next(int exclusiveMax)
        {
            return exclusiveMax == 1000 ? 999 : 0;
        }
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
            .Select(row =>
                $"{row.Item.Value} x {row.Quantity}" + (row.Slot == EquipmentSlot.None ? string.Empty : " worn"))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToArray();
    }

    private void Place(string name, WorldPosition position)
    {
        Execute(
            $"UPDATE characters SET map_definition_id = '{UmbralGrotto}', position_x = {Number(position.X)}, "
            + $"position_y = {Number(position.Y)}, position_z = {Number(position.Z)} WHERE name = '{name}'");
    }

    private static string Number(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
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
    public void TwoPlayers_InTheGrotto_FightWithTheirSkills_AndKeepTheirBuildsThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            DungeonPackage.Build(
                DungeonPackage.Disarm(GrottoCrawler),
                DungeonPackage.Expose(GrottoCrawler),
                DungeonPackage.NeverCast(GrottoCrawler, 0)));

        ServerContent content;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var sora = new SocketClient(content, SoraIdentity, SoraName);
            using var ines = new SocketClient(content, InesIdentity, InesName);
            EnterTheHall(port, sora, ines);
            AssertSees("into the grotto", sora, ines, InesName);
            AssertSees("into the grotto", ines, sora, SoraName);
            AssertCleanTraffic("into the grotto", sora, ines);

            RaiseIronGuard(sora, ines);
            MendSora(ines, sora);
            AssertCleanTraffic("Mend", sora, ines);
            EntityId crawler = FrostLanceOnACrawler(ines, sora);
            AssertCleanTraffic("Frost Lance", sora, ines);
            HeavyBlowsOn(crawler, sora, ines);
            AssertCleanTraffic("Heavy Blow", sora, ines);
            Stop(host, sora, ines);
        }

        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var sora = new SocketClient(content, SoraIdentity, SoraName);
            using var ines = new SocketClient(content, InesIdentity, InesName);
            sora.EnterWorld(port);
            ines.EnterWorld(port);
            foreach (SocketClient client in new[] { sora, ines })
            {
                Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(UmbralGrotto)), "restart: in the grotto");
            }

            AssertBuild("restart", sora, Vanguard, SoraBuild);
            AssertBuild("restart", ines, Arcanist, InesBuild);
            AssertIronGuardKept(sora, ines);
            AssertCleanTraffic("restart", sora, ines);
            Stop(host, sora, ines);
        }
    }
}
}
