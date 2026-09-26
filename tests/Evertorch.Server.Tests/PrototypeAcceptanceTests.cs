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
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 6 prototype acceptance (Prototype Content §9) with two players, end to end: the composed server
///     host on a real PostgreSQL 18, real UDP sockets on loopback, and two clients built from the client's production
///     networking and gameplay code, pumped side by side on the test's thread. Each later line of Milestone 6 adds its
///     steps here.
/// </summary>
/// <remarks>
///     Combat and drops draw from scripted sources (<see cref="TestHosts.ScriptOutcomes" />): every attack hits
///     without a critical, and every slime drops one gel. Monster placement and AI keep the seeded server source.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class PrototypeAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingField = "map.training_field";
    private const string Adventurer = "job.adventurer";
    private const string TrainingSlime = "monster.training_slime";
    private const string ForestCrawler = "monster.forest_crawler";
    private const string SparkWisp = "monster.spark_wisp";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string ClothArmor = "item.armor.cloth";
    private const float ConvergedDistance = 1e-3f;
    private const float WalkedDistance = 2f;

    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(40);

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

    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=5" },
            contentRootPath,
            m_database.ConnectionString);
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    /// <summary>
    ///     Both players enter the training ground, see each other, walk, fight, and cross to the training field, where
    ///     a crawler goes for one of them and drops a sword and armor they put on, a wisp answers from its range, and
    ///     one reconnects; the server then stops. Returns each character as the server last showed it, by character
    ///     name.
    /// </summary>
    private static Dictionary<string, PlayerSummary> PlayTogether(IHost host, string connectionString)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        var stopped = new Dictionary<string, PlayerSummary>();

        using var first = new SocketClient(content, Players.FirstIdentity, Players.FirstName);
        using var second = new SocketClient(content, Players.SecondIdentity, Players.SecondName);
        EnterTogether(port, first, second);
        foreach (SocketClient client in new[] { first, second })
        {
            Assert.That(
                client.World.Map,
                Is.EqualTo(new MapDefinitionId(TrainingGround)),
                "enter: both players are in the training ground");
        }

        SeeEachOther("enter", first, second);
        WatchEachOtherWalk(first, second);
        WalkOnALossyLink(admin, first, second);
        FightSlimes("fight", first, second);
        ShareAKill(content, admin, first, second);
        UseSkills(first, second);
        FocusQuickensTheSwing(first, second);
        CrossToTheField(content, admin, first, second);
        FightACrawler(content, admin, connectionString, first, second);
        ProvokeAWisp(content, first, second);

        EntityId firstEntity = first.World.LocalEntity;
        EntityId secondEntity = second.World.LocalEntity;
        var despawned = new List<EntityId>();
        second.World.RemoteDespawned += remote => despawned.Add(remote.Entity);
        first.Disconnect();
        SocketClients.PumpFor(TimeSpan.FromMilliseconds(500), second);
        Assert.That(
            second.World.Remotes.ContainsKey(firstEntity),
            Is.True,
            "reconnect: the other player still sees the retained character");

        using (var again = new SocketClient(content, Players.FirstIdentity, Players.FirstName))
        {
            again.Connect(port);
            Assert.That(
                SocketClients.PumpUntil(() => again.Connection.World?.Inventory.IsCurrent == true, again, second),
                Is.True,
                $"reconnect: entered again: {again.Connection.LocalError} {again.Connection.DisconnectCause}");
            Assert.That(again.World.LocalEntity, Is.EqualTo(firstEntity), "reconnect: attached to the same entity");
            SeeEachOther("reconnect", again, second);
            Assert.That(despawned, Does.Not.Contain(firstEntity), "reconnect: the other player never lost sight");
            AwaitConvergence(admin, "reconnect", again, second);
            stopped[Players.FirstName] = SummaryOf(admin, firstEntity);
            stopped[Players.SecondName] = SummaryOf(admin, secondEntity);

            host.StopAsync().GetAwaiter().GetResult();
            foreach (SocketClient client in new[] { again, second })
            {
                Assert.That(
                    SocketClients.PumpUntil(
                        () => client.Connection.State == ClientConnectionState.Disconnected,
                        client),
                    Is.True,
                    "restart: both clients heard the server stop");
                Assert.That(client.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Maintenance), "restart");
                AssertCleanTraffic(client, "together");
            }
        }

        AssertCleanTraffic(first, "together");
        return stopped;
    }

    /// <summary>
    ///     After a clean stop and a second server on the same database, both sign in again to the same characters on
    ///     the map and at the place where they stood, at the level and experience they had, and see each other.
    /// </summary>
    private static void PlayAfterTheRestart(IHost host, IReadOnlyDictionary<string, PlayerSummary> stopped)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        using var first = new SocketClient(content, Players.FirstIdentity, Players.FirstName);
        using var second = new SocketClient(content, Players.SecondIdentity, Players.SecondName);
        EnterTogether(port, first, second);
        (SocketClient Client, string Name)[] characters =
        {
            (first, Players.FirstName), (second, Players.SecondName)
        };
        foreach ((SocketClient client, string name) in characters)
        {
            Assert.That(client.Connection.Characters.Single().Name, Is.EqualTo(name), "restart: the same character");
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingField)), "restart: on the field");
            Assert.That(
                client.DistanceTo(stopped[name].Position),
                Is.LessThanOrEqualTo(ConvergedDistance),
                $"restart: {name} stands where the shutdown checkpoint left it");
        }

        bool isKept = SocketClients.PumpUntil(
            () => characters.All(character =>
            {
                PlayerSummary? now = admin.GetPlayers(AdminActor.LocalConsole)
                    .SingleOrDefault(player => player.Entity == character.Client.World.LocalEntity);
                PlayerSummary before = stopped[character.Name];
                return now != null && now.Level == before.Level && now.Experience == before.Experience;
            }),
            first,
            second);
        Assert.That(isKept, Is.True, "restart: both characters kept their level and experience");
        Assert.That(
            first.World.Inventory.Rows.Count(row => row.Slot == EquipmentSlot.Weapon),
            Is.EqualTo(1),
            "restart: the first player still wears the sword");
        Assert.That(
            second.World.Inventory.Rows.Count(row => row.Slot == EquipmentSlot.Armor),
            Is.EqualTo(1),
            "restart: the second player still wears the armor");

        SeeEachOther("restart", first, second);
        AssertCleanTraffic(first, "restart");
        AssertCleanTraffic(second, "restart");
    }

    private static void EnterTogether(int port, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            client.Connect(port);
        }

        bool isEntered = SocketClients.PumpUntil(
            () => clients.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            clients);
        string states = string.Join(
            ", ",
            clients.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"enter: both players entered the world ({states})");
    }

    // Each client has a spawn for the other, of the other's job, and draws it: interest management put them in view.
    private static void SeeEachOther(string step, SocketClient first, SocketClient second)
    {
        bool isSeen = SocketClients.PumpUntil(
            () => Draws(first, second.World.LocalEntity) != null && Draws(second, first.World.LocalEntity) != null,
            first,
            second);
        Assert.That(isSeen, Is.True, $"{step}: each player sees the other");
        foreach ((SocketClient viewer, SocketClient seen) in new[] { (first, second), (second, first) })
        {
            RemoteEntity remote = viewer.World.Remotes[seen.World.LocalEntity];
            Assert.That(remote.Kind, Is.EqualTo(EntityKind.Player), step);
            Assert.That(remote.DefinitionId, Is.EqualTo(Adventurer), step);
        }
    }

    // One walks while the other watches, then the other way round: each sees the other move.
    private static void WatchEachOtherWalk(SocketClient first, SocketClient second)
    {
        WalkWhileWatched(second, first, 1f, 0f);
        WalkWhileWatched(first, second, -1f, 0f);
    }

    private static void WalkWhileWatched(SocketClient walker, SocketClient watcher, float directionX, float directionZ)
    {
        EntityId walking = walker.World.LocalEntity;
        WorldPosition before = Draws(watcher, walking)!.Value;
        walker.Controller.SetManualDirection(directionX, directionZ);
        SocketClients.PumpFor(TimeSpan.FromSeconds(1), walker, watcher);
        walker.Controller.SetManualDirection(0f, 0f);
        bool hasMoved = SocketClients.PumpUntil(
            () => Horizontal(Draws(watcher, walking) ?? before, before) > WalkedDistance,
            walker,
            watcher);
        Assert.That(hasMoved, Is.True, "walk: each player sees the other move");
    }

    // Both walk at once over links that delay, reorder, and lose messages, then stop: each client ends where the
    // server has its own character, and draws the other where the server has that one.
    private static void WalkOnALossyLink(IAdminCommandService admin, SocketClient first, SocketClient second)
    {
        foreach (SocketClient client in new[] { first, second })
        {
            client.Link.LatencyMilliseconds = 50;
            client.Link.JitterMilliseconds = 10;
            client.Link.LossPercent = 10;
            client.Link.ReorderPercent = 5;
        }

        int dropped = first.Link.Dropped + second.Link.Dropped;
        first.Controller.SetManualDirection(0f, 1f);
        second.Controller.SetManualDirection(1f, 0f);
        SocketClients.PumpFor(TimeSpan.FromSeconds(1.5), first, second);
        first.Controller.SetManualDirection(0f, 0f);
        second.Controller.SetManualDirection(0f, 0f);
        AwaitConvergence(admin, "lossy walk", first, second);
        Assert.That(
            first.Link.Dropped + second.Link.Dropped,
            Is.GreaterThan(dropped),
            "lossy walk: messages were lost");

        foreach (SocketClient client in new[] { first, second })
        {
            Assert.That(client.World.Smoother.Snaps, Is.Zero, "lossy walk: no correction was large enough to snap");
            client.Link.LatencyMilliseconds = 0;
            client.Link.JitterMilliseconds = 0;
            client.Link.LossPercent = 0;
            client.Link.ReorderPercent = 0;
        }
    }

    // Tab, then West once the server confirms the target, for both players at once: each one's slime dies.
    private static void FightSlimes(string step, params SocketClient[] clients)
    {
        var targets = new Dictionary<SocketClient, EntityId>();
        var deaths = new Dictionary<SocketClient, List<EntityId>>();
        foreach (SocketClient client in clients)
        {
            var died = new List<EntityId>();
            deaths[client] = died;
            client.World.EntityDiedReceived += death => died.Add(death.Entity);
            EntityId slime = client.CycleTarget(true);
            Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
            Assert.That(client.World.Remotes[slime].DefinitionId, Is.EqualTo(TrainingSlime), step);
            targets[client] = slime;
        }

        Assert.That(
            SocketClients.PumpUntil(() => clients.All(client => client.World.Target == targets[client]), clients),
            Is.True,
            $"{step}: the server confirmed each target");
        foreach (SocketClient client in clients)
        {
            client.AttackTarget();
        }

        bool isKilled = SocketClients.PumpUntil(
            () => clients.All(client => deaths[client].Contains(targets[client])),
            FightLimit,
            clients);
        Assert.That(isKilled, Is.True, $"{step}: each player's slime died");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.IsLocalDead, Is.False, $"{step}: both players survived");
        }
    }

    // Both players attack one slime. Once it dies, each character's level and experience in the server's players view
    // are what its share of all the damage the slime took gives it (Gameplay Systems §2.1).
    private static void ShareAKill(
        ServerContent content,
        IAdminCommandService admin,
        SocketClient first,
        SocketClient second)
    {
        const string step = "share";
        SocketClient[] clients = { first, second };
        EntityId slime = first.CycleTarget(true);
        Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
        Assert.That(
            SocketClients.PumpUntil(() => second.World.Remotes.ContainsKey(slime), clients),
            Is.True,
            $"{step}: both players see the slime");
        second.Connection.SendTarget(slime);
        Assert.That(
            SocketClients.PumpUntil(() => clients.All(client => client.World.Target == slime), clients),
            Is.True,
            $"{step}: the server confirmed the shared target");

        // The players view is republished once a second; two of those carry the earlier fight's experience.
        SocketClients.PumpFor(TimeSpan.FromSeconds(2.2), clients);
        IReadOnlyList<PlayerSummary> before = admin.GetPlayers(AdminActor.LocalConsole);
        var dealt = new Dictionary<EntityId, long>();
        bool isDead = false;
        first.World.DamageReceived += damage =>
        {
            if (damage.Target == slime)
            {
                dealt.TryGetValue(damage.Source, out long sum);
                dealt[damage.Source] = sum + damage.Amount;
            }
        };
        first.World.EntityDiedReceived += death => isDead |= death.Entity == slime;
        foreach (SocketClient client in clients)
        {
            client.AttackTarget();
        }

        Assert.That(SocketClients.PumpUntil(() => isDead, FightLimit, clients), Is.True, $"{step}: the slime died");

        long baseExperience = content.Monsters[new MonsterDefinitionId(TrainingSlime)].BaseExperience;
        ExperienceDefinitionId tableId = content.Jobs[new JobDefinitionId(Adventurer)].ExperienceTable;
        var rules = new RenewalProgressionRules();
        long total = dealt.Values.Sum();
        var expected = new Dictionary<EntityId, LevelProgress>();
        foreach (SocketClient client in clients)
        {
            EntityId entity = client.World.LocalEntity;
            Assert.That(dealt.TryGetValue(entity, out long damage), Is.True, $"{step}: both players hit the slime");
            PlayerSummary was = before.Single(player => player.Entity == entity);
            long share = rules.ShareExperience(baseExperience, damage, total);
            expected[entity] = rules.AddExperience(
                content.ExperienceTables[tableId],
                new LevelProgress(was.Level, was.Experience),
                share);
        }

        bool isShared = SocketClients.PumpUntil(
            () => admin.GetPlayers(AdminActor.LocalConsole).Count(player =>
                expected.TryGetValue(player.Entity, out LevelProgress progress)
                && player.Level == progress.Level
                && player.Experience == progress.Experience) == expected.Count,
            clients);
        string shares = string.Join(", ", dealt.Select(pair => $"{pair.Key.Value} dealt {pair.Value}"));
        Assert.That(isShared, Is.True, $"{step}: each character got its share ({shares} of {total})");

        // Each client hears its own progress (Network Protocol §9), and its SP arrived with its HP.
        bool isSeen = SocketClients.PumpUntil(
            () => clients.All(client =>
                client.World.Level == expected[client.World.LocalEntity].Level
                && client.World.Experience == (ulong)expected[client.World.LocalEntity].Experience),
            clients);
        Assert.That(isSeen, Is.True, $"{step}: each client shows its own level and experience");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.LocalMaximumSpirit, Is.GreaterThan(0u), $"{step}: SP reached the client");
            Assert.That(client.World.ExperienceToNextLevel, Is.GreaterThan(client.World.Experience), step);
        }
    }

    // The first player's skill state walks it within Strike's range of a slime and strikes it, then heals it with
    // First Aid; both clients see each cast begin and resolve (Gameplay Systems §5.1; Network Protocol §9).
    private static void UseSkills(SocketClient first, SocketClient second)
    {
        const string step = "skills";
        SocketClient[] clients = { first, second };
        var started = new Dictionary<SocketClient, List<SkillCastStarted>>();
        var resolved = new Dictionary<SocketClient, List<SkillResolved>>();
        foreach (SocketClient client in clients)
        {
            var casts = new List<SkillCastStarted>();
            var results = new List<SkillResolved>();
            started[client] = casts;
            resolved[client] = results;
            client.World.SkillCastStartedReceived += casts.Add;
            client.World.SkillResolvedReceived += results.Add;
        }

        EntityId slime = first.CycleTarget(true);
        Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
        Assert.That(
            SocketClients.PumpUntil(() => first.World.Target == slime, clients),
            Is.True,
            $"{step}: the server confirmed the target");

        var strike = new SkillDefinitionId("skill.strike");
        var firstAid = new SkillDefinitionId("skill.first_aid");
        EntityId caster = first.World.LocalEntity;
        Assert.That(first.UseSkill(strike), Is.True, $"{step}: Strike on the confirmed target");
        Assert.That(
            SocketClients.PumpUntil(
                () => clients.All(client => resolved[client].Any(result => result.Skill == strike)),
                clients),
            Is.True,
            $"{step}: both clients saw Strike resolve");
        SocketClients.PumpFor(TimeSpan.FromSeconds(1), clients);
        Assert.That(first.UseSkill(firstAid), Is.True, $"{step}: First Aid");
        Assert.That(
            SocketClients.PumpUntil(
                () => clients.All(client => resolved[client].Any(result => result.Skill == firstAid)),
                clients),
            Is.True,
            $"{step}: both clients saw First Aid resolve");

        foreach (SocketClient client in clients)
        {
            SkillCastStarted strikeStart = started[client].Single(cast => cast.Skill == strike);
            SkillResolved strikeResult = resolved[client].Single(result => result.Skill == strike);
            Assert.That((strikeStart.Caster, strikeStart.Target), Is.EqualTo((caster, slime)), step);
            Assert.That((strikeResult.Caster, strikeResult.Target, strikeResult.Outcome),
                Is.EqualTo((caster, slime, SkillOutcome.Hit)), $"{step}: every attack hits in this scenario");
            SkillCastStarted aidStart = started[client].Single(cast => cast.Skill == firstAid);
            SkillResolved aidResult = resolved[client].Single(result => result.Skill == firstAid);
            Assert.That((aidStart.Caster, aidStart.Target, aidStart.CastMs),
                Is.EqualTo((caster, default(EntityId), 1331u)), step);
            Assert.That((aidResult.Target, aidResult.Outcome, aidResult.Amount),
                Is.EqualTo((caster, SkillOutcome.Healed, 15u)), $"{step}: the nominal heal");
        }

        Assert.That(
            first.World.Skills.Select(entry => entry.Skill.Value),
            Is.EqualTo(new[] { "skill.strike", "skill.first_aid", "skill.focus" }),
            step);
        Assert.That(first.Skill.SkillsSent, Is.EqualTo(2), $"{step}: both through the skill state, once each");
    }

    // The second player, whose SP the other skills left untouched, casts Focus: its client hears of the effect, and
    // its next swing at a slime comes at the shorter interval of the research note's vector, 940 ms to 920 ms
    // (Gameplay Systems §9.1).
    private static void FocusQuickensTheSwing(SocketClient first, SocketClient second)
    {
        const string step = "focus";
        SocketClient[] clients = { first, second };
        var focus = new SkillDefinitionId("skill.focus");
        var status = new StatusDefinitionId("status.focus");
        var swings = new List<AttackStarted>();
        var observed = new List<SkillResolved>();
        second.World.AttackStartedReceived += started =>
        {
            if (started.Attacker == second.World.LocalEntity)
            {
                swings.Add(started);
            }
        };
        first.World.SkillResolvedReceived += observed.Add;

        EntityId slime = second.CycleTarget(true);
        Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
        Assert.That(
            SocketClients.PumpUntil(() => second.World.Target == slime, clients),
            Is.True,
            $"{step}: the server confirmed the target");
        Assert.That(second.UseSkill(focus), Is.True, $"{step}: Focus");
        Assert.That(
            SocketClients.PumpUntil(() => second.World.StatusRemaining(status) > 0, clients),
            Is.True,
            $"{step}: the owner heard of the effect");
        swings.Clear();
        second.AttackTarget();
        Assert.That(SocketClients.PumpUntil(() => swings.Count > 0, clients), Is.True, $"{step}: a swing began");

        Assert.That(swings[0].Timing.Interval, Is.EqualTo(TimeSpan.FromMilliseconds(920)), step);
        Assert.That(second.World.StatusRemaining(status), Is.InRange(50.0, 60.0), step);
        Assert.That(
            observed.Where(result => result.Skill == focus).Select(result => (result.Outcome, result.Amount)),
            Is.EqualTo(new[] { (SkillOutcome.Applied, 0u) }),
            $"{step}: the other client sees the effect applied, and nothing about it");
        Assert.That(first.World.StatusEffects, Is.Empty, $"{step}: only the owner hears of its effects");
        second.AutoAttack.OnWalkRequested();
        SocketClients.PumpFor(TimeSpan.FromSeconds(1), clients);
    }

    // Both players line up before the ground's east gate and hold east through its portal over links with 100 ms of
    // latency and 10 % loss. Each client follows its character to the field without a freeze, walks on there, and
    // agrees with the server, which dropped input made for the ground (Gameplay Systems §4.2, §5.1; Network Protocol
    // §3, §10).
    private static void CrossToTheField(
        ServerContent content,
        IAdminCommandService admin,
        SocketClient first,
        SocketClient second)
    {
        const string step = "cross";
        SocketClient[] clients = { first, second };
        var field = new MapDefinitionId(TrainingField);
        MapPortal portal = content.Maps[new MapDefinitionId(TrainingGround)].Portals.Single();
        var lineUp = new Dictionary<SocketClient, WorldPosition>
        {
            [first] = new(portal.Center.X - 5.5f, 0f, 0.4f),
            [second] = new(portal.Center.X - 5.5f, 0f, -0.4f)
        };
        foreach (SocketClient client in clients)
        {
            Assert.That(
                client.Controller.TryMoveTo(client.World.Predictor.Position, lineUp[client]),
                Is.True,
                $"{step}: a way to the gate");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => clients.All(client => !client.Controller.HasPath && client.DistanceTo(lineUp[client]) < 0.5f),
                clients),
            Is.True,
            $"{step}: both lined up before the gate: " + string.Join(
                ", ",
                clients.Select(client =>
                    $"{client.World.Predictor.Position} path {client.Controller.HasPath} locked "
                    + $"{client.Controller.IsLocked} dead {client.World.IsLocalDead} chasing {client.Controller.IsChasing}")));

        foreach (SocketClient client in clients)
        {
            client.Link.LatencyMilliseconds = 100;
            client.Link.LossPercent = 10;
            client.Hold(1f, 0f);
        }

        Assert.That(
            SocketClients.PumpUntil(() => clients.All(client => client.World.Map == field), clients),
            Is.True,
            $"{step}: both clients followed their characters to the field");
        SocketClients.PumpFor(TimeSpan.FromSeconds(1), clients);
        foreach (SocketClient client in clients)
        {
            client.Hold(0f, 0f);
        }

        AwaitConvergence(admin, step, first, second);
        IReadOnlyList<PlayerSummary> server = admin.GetPlayers(AdminActor.LocalConsole);
        foreach (SocketClient client in clients)
        {
            PlayerSummary summary = server.Single(player => player.Entity == client.World.LocalEntity);
            Assert.That(summary.Map, Is.EqualTo(field), $"{step}: the server has the character on the field");
            Assert.That(
                summary.Position.X,
                Is.GreaterThan(portal.DestinationPosition.X + WalkedDistance),
                $"{step}: the held direction walked it on from the arrival");
            Assert.That(client.Connection.MapEpoch, Is.EqualTo(1), step);
            Assert.That(client.World.Smoother.Snaps, Is.Zero, $"{step}: no correction on the field was a snap");
            client.Link.LatencyMilliseconds = 0;
            client.Link.LossPercent = 0;
        }

        Assert.That(
            server.Sum(player => player.OtherEpochInputs),
            Is.GreaterThan(0),
            $"{step}: input made for the ground reached the server after the crossing and was dropped");
    }

    // On the field a crawler goes for a player unprovoked (Gameplay Systems §10). The first player steps toward the
    // crawler nearest the staging point, 12.5 m from the crawlers' home, and once it attacks leads it back there, out
    // of the other crawlers' perception while they are home; both players kill it, each takes its share of the
    // experience, and both walk back beyond the crawlers' leash.
    private static void FightACrawler(
        ServerContent content,
        IAdminCommandService admin,
        string connectionString,
        SocketClient first,
        SocketClient second)
    {
        const string step = "crawler";
        SocketClient[] clients = { first, second };
        var crawlerId = new MonsterDefinitionId(ForestCrawler);
        WorldPosition home = content.Maps[new MapDefinitionId(TrainingField)].MonsterSpawns
            .Single(spawn => spawn.Monster == crawlerId)
            .Center;
        WorldPosition start = first.World.Predictor.Position;
        float awayX = start.X - home.X;
        float awayZ = start.Z - home.Z;
        float away = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
        var staging = new WorldPosition(home.X + awayX / away * 12.5f, 0f, home.Z + awayZ / away * 12.5f);
        var places = new Dictionary<SocketClient, WorldPosition>
        {
            [first] = new(staging.X, 0f, staging.Z + 0.5f),
            [second] = new(staging.X, 0f, staging.Z - 0.5f)
        };
        WalkTo(step, places, clients);

        var attacks = new List<AttackStarted>();
        var deaths = new List<EntityId>();
        first.World.AttackStartedReceived += attacks.Add;
        first.World.EntityDiedReceived += death => deaths.Add(death.Entity);

        bool IsCrawler(EntityId entity)
        {
            return first.World.Remotes.TryGetValue(entity, out RemoteEntity? remote)
                && remote.DefinitionId == ForestCrawler;
        }

        bool IsAttackedByACrawler()
        {
            return attacks.Any(attack => IsCrawler(attack.Attacker) && attack.Target == first.World.LocalEntity);
        }

        // Each try walks to 4.5 m from a crawler, within its perception, and waits there for it to come; aiming again
        // while it comes would only keep the player out of its reach.
        var sinceStep = Stopwatch.StartNew();
        var tries = new List<string>();
        while (!IsAttackedByACrawler() && sinceStep.Elapsed < FightLimit)
        {
            RemoteEntity? nearest = first.World.Remotes.Values
                .Where(remote => remote.DefinitionId == ForestCrawler && !remote.IsDead)
                .OrderBy(remote => Horizontal(Drawn(first, remote), staging))
                .FirstOrDefault();
            Assert.That(nearest, Is.Not.Null, $"{step}: the first player sees a crawler");
            WorldPosition crawler = Drawn(first, nearest!);
            float towardX = staging.X - crawler.X;
            float towardZ = staging.Z - crawler.Z;
            float toward = (float)Math.Sqrt(towardX * towardX + towardZ * towardZ);
            var goal = new WorldPosition(crawler.X + towardX / toward * 4.5f, 0f, crawler.Z + towardZ / toward * 4.5f);
            bool isWalking = first.Controller.TryMoveTo(first.World.Predictor.Position, goal);
            tries.Add($"{nearest!.Entity.Value} at {crawler} goal {goal} walking {isWalking}");
            SocketClients.PumpUntil(IsAttackedByACrawler, TimeSpan.FromSeconds(4), clients);
        }

        Assert.That(
            IsAttackedByACrawler(),
            Is.True,
            $"{step}: a crawler went for the first player; tries: {string.Join("; ", tries)}; attacks: "
            + string.Join(", ", attacks.Select(attack => $"{attack.Attacker.Value}>{attack.Target.Value}")));
        AttackStarted unprovoked = attacks.First(attack => IsCrawler(attack.Attacker));
        EntityId target = unprovoked.Attacker;
        Assert.That(
            clients.Select(client => client.World.Target),
            Is.All.EqualTo(default(EntityId)),
            $"{step}: neither player had targeted anything when it attacked");

        IReadOnlyList<PlayerSummary> before = admin.GetPlayers(AdminActor.LocalConsole);
        WalkTo(step, new Dictionary<SocketClient, WorldPosition> { [first] = places[first] }, clients);
        foreach (SocketClient client in clients)
        {
            client.Connection.SendTarget(target);
        }

        Assert.That(
            SocketClients.PumpUntil(() => clients.All(client => client.World.Target == target), clients),
            Is.True,
            $"{step}: the server confirmed the crawler as both players' target");
        foreach (SocketClient client in clients)
        {
            client.AttackTarget();
        }

        Assert.That(
            SocketClients.PumpUntil(() => deaths.Contains(target), FightLimit, clients),
            Is.True,
            $"{step}: the crawler died");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.IsLocalDead, Is.False, $"{step}: both players survived");
            client.AutoAttack.OnWalkRequested();
        }

        // The players view is republished once a second.
        bool isShared = SocketClients.PumpUntil(
            () => clients.All(client =>
            {
                PlayerSummary was = before.Single(player => player.Entity == client.World.LocalEntity);
                PlayerSummary? now = admin.GetPlayers(AdminActor.LocalConsole)
                    .SingleOrDefault(player => player.Entity == client.World.LocalEntity);
                return now != null && (now.Level > was.Level || now.Experience > was.Experience);
            }),
            clients);
        Assert.That(isShared, Is.True, $"{step}: both players got a share of the crawler's experience");
        EquipTheCrawlersDrops(connectionString, first, second);

        var back = new Dictionary<SocketClient, WorldPosition>
        {
            [first] = new(start.X, 0f, start.Z),
            [second] = new(start.X, 0f, start.Z - 0.8f)
        };
        WalkTo(step, back, clients);
    }

    // Every drop is scripted in, so the crawler left a training sword and cloth armor. Once its loot priority window
    // has passed, the first player picks the sword up and the second the armor, and each equips its own. Each client
    // sees its row worn, and PostgreSQL holds both (Gameplay Systems §11.1, Persistence §5).
    private static void EquipTheCrawlersDrops(string connectionString, SocketClient first, SocketClient second)
    {
        const string step = "equip";
        SocketClient[] clients = { first, second };
        SocketClients.PumpFor(TimeSpan.FromMilliseconds(PickupSystem.LootPriorityMs), clients);

        long sword = PickUpAndEquip(step, first, TrainingSword, EquipmentSlot.Weapon, clients);
        long armor = PickUpAndEquip(step, second, ClothArmor, EquipmentSlot.Armor, clients);

        Assert.That(
            WornRow(connectionString, Players.FirstName, EquipmentSlot.Weapon),
            Is.EqualTo(sword),
            $"{step}: PostgreSQL holds the sword in the first player's weapon slot");
        Assert.That(
            WornRow(connectionString, Players.SecondName, EquipmentSlot.Armor),
            Is.EqualTo(armor),
            $"{step}: PostgreSQL holds the armor in the second player's armor slot");
    }

    private static long PickUpAndEquip(
        string step,
        SocketClient player,
        string item,
        EquipmentSlot slot,
        SocketClient[] clients)
    {
        RemoteEntity? drop = player.World.Remotes.Values.FirstOrDefault(remote => remote.DefinitionId == item);
        Assert.That(drop, Is.Not.Null, $"{step}: {item} lies where the crawler died");
        player.Pickup.Pickup(drop!.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => player.World.Inventory.Rows.Any(row => row.Item.Value == item), clients),
            Is.True,
            $"{step}: {item} was picked up");
        long row = player.World.Inventory.Rows.Single(entry => entry.Item.Value == item).InventoryItem;

        player.Connection.SendEquip(row);

        Assert.That(
            SocketClients.PumpUntil(
                () => player.World.Inventory.Rows.Any(entry => entry.InventoryItem == row && entry.Slot == slot),
                clients),
            Is.True,
            $"{step}: its client sees {item} worn");
        return row;
    }

    private static long WornRow(string connectionString, string character, EquipmentSlot slot)
    {
        using (var connection = new NpgsqlConnection(connectionString))
        using (var command = new NpgsqlCommand(
                   "SELECT coalesce(max(e.inventory_item_id), 0) FROM equipment e "
                   + "JOIN characters c ON c.id = e.character_id WHERE c.name = @name AND e.slot = @slot",
                   connection))
        {
            connection.Open();
            command.Parameters.AddWithValue("name", character);
            command.Parameters.AddWithValue("slot", slot.ToString());
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    // A spark wisp keeps its range (Gameplay Systems §10). The first player hits the wisp nearest the staging point
    // once and steps back to 5 m, beyond its keep distance and within its reach; the wisp answers from there with its
    // basic attack and, within the time allowed, a Spark Bolt both clients see. Both players then walk back beyond
    // its leash.
    private static void ProvokeAWisp(ServerContent content, SocketClient first, SocketClient second)
    {
        const string step = "wisp";
        SocketClient[] clients = { first, second };
        var wispId = new MonsterDefinitionId(SparkWisp);
        var sparkBolt = new SkillDefinitionId("skill.spark_bolt");
        WorldPosition home = content.Maps[new MapDefinitionId(TrainingField)].MonsterSpawns
            .Single(spawn => spawn.Monster == wispId)
            .Center;
        WorldPosition start = first.World.Predictor.Position;
        float awayX = start.X - home.X;
        float awayZ = start.Z - home.Z;
        float away = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
        var staging = new WorldPosition(home.X + awayX / away * 10f, 0f, home.Z + awayZ / away * 10f);
        WalkTo(
            step,
            new Dictionary<SocketClient, WorldPosition>
            {
                [first] = new(staging.X, 0f, staging.Z + 0.5f),
                [second] = new(staging.X, 0f, staging.Z - 0.5f)
            },
            clients);

        RemoteEntity? wisp = first.World.Remotes.Values
            .Where(remote => remote.DefinitionId == SparkWisp && !remote.IsDead)
            .OrderBy(remote => Horizontal(Drawn(first, remote), staging))
            .FirstOrDefault();
        Assert.That(wisp, Is.Not.Null, $"{step}: the first player sees a wisp");
        EntityId target = wisp!.Entity;
        bool isHit = false;
        float farthestAttack = 0f;
        var casts = new Dictionary<SocketClient, bool> { [first] = false, [second] = false };
        bool isBoltResolved = false;
        first.World.DamageReceived += damage =>
            isHit |= damage.Source == first.World.LocalEntity && damage.Target == target;
        first.World.AttackStartedReceived += started =>
        {
            if (started.Attacker == target && started.Target == first.World.LocalEntity)
            {
                farthestAttack = Math.Max(
                    farthestAttack,
                    Horizontal(first.World.Predictor.Position, Drawn(first, wisp)));
            }
        };
        foreach (SocketClient client in clients)
        {
            client.World.SkillCastStartedReceived += cast =>
                casts[client] |= cast.Caster == target && cast.Skill == sparkBolt;
        }

        first.World.SkillResolvedReceived += resolved =>
            isBoltResolved |= resolved.Caster == target
                && resolved.Skill == sparkBolt
                && resolved.Target == first.World.LocalEntity
                && resolved.Outcome == SkillOutcome.Hit;

        first.Connection.SendTarget(target);
        Assert.That(
            SocketClients.PumpUntil(() => first.World.Target == target, clients),
            Is.True,
            $"{step}: the server confirmed the wisp as the target");
        first.AttackTarget();
        Assert.That(SocketClients.PumpUntil(() => isHit, FightLimit, clients), Is.True, $"{step}: the player hit it");
        first.AutoAttack.OnWalkRequested();

        WorldPosition drawn = Drawn(first, wisp);
        float backX = staging.X - drawn.X;
        float backZ = staging.Z - drawn.Z;
        float back = (float)Math.Sqrt(backX * backX + backZ * backZ);
        WalkTo(
            step,
            new Dictionary<SocketClient, WorldPosition>
            {
                [first] = new(drawn.X + backX / back * 5f, 0f, drawn.Z + backZ / back * 5f)
            },
            clients);
        bool isAnswered = SocketClients.PumpUntil(
            () => farthestAttack > 2f && casts.Values.All(isSeen => isSeen) && isBoltResolved,
            FightLimit,
            clients);
        Assert.That(
            isAnswered,
            Is.True,
            $"{step}: a ranged attack ({farthestAttack} m) and a Spark Bolt both clients saw ({casts[first]}, "
            + $"{casts[second]}) that hit ({isBoltResolved})");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.IsLocalDead, Is.False, $"{step}: both players survived");
        }

        WalkTo(
            step,
            new Dictionary<SocketClient, WorldPosition>
            {
                [first] = new(start.X, 0f, start.Z),
                [second] = new(start.X, 0f, start.Z - 0.8f)
            },
            clients);
    }

    // Starts each walk, then pumps until every walker has arrived within a step of its goal.
    private static void WalkTo(
        string step,
        IReadOnlyDictionary<SocketClient, WorldPosition> goals,
        params SocketClient[] clients)
    {
        foreach (KeyValuePair<SocketClient, WorldPosition> goal in goals)
        {
            Assert.That(
                goal.Key.Controller.TryMoveTo(goal.Key.World.Predictor.Position, goal.Value),
                Is.True,
                $"{step}: a way to {goal.Value}");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => goals.All(goal => !goal.Key.Controller.HasPath && goal.Key.DistanceTo(goal.Value) < 0.5f),
                clients),
            Is.True,
            $"{step}: every walker arrived");
    }

    private static WorldPosition Drawn(SocketClient viewer, RemoteEntity remote)
    {
        return remote.Buffer.TrySample(viewer.World.RemoteRenderTime, out WorldPosition position, out WorldDirection _)
            ? position
            : default;
    }

    private static void AwaitConvergence(IAdminCommandService admin, string step, params SocketClient[] clients)
    {
        // The admin view is republished once a second, so agreement shows up within two of those.
        bool isConverged = SocketClients.PumpUntil(
            () => clients.All(client => HasConverged(client, clients, admin.GetPlayers(AdminActor.LocalConsole))),
            clients);
        string serverView = string.Join(
            ", ",
            admin.GetPlayers(AdminActor.LocalConsole).Select(player => $"{player.Entity.Value} {player.Position}"));
        Assert.That(isConverged, Is.True, $"{step}: every client agrees with the server at {serverView}");
    }

    // A client's own character where its prediction is, and every other character where it draws it.
    private static bool HasConverged(
        SocketClient client,
        IEnumerable<SocketClient> everyone,
        IReadOnlyList<PlayerSummary> server)
    {
        ClientWorld world = client.World;
        if (world.Predictor.PendingCount != 0)
        {
            return false;
        }

        foreach (SocketClient other in everyone)
        {
            EntityId entity = other.World.LocalEntity;
            PlayerSummary? summary = server.FirstOrDefault(player => player.Entity == entity);
            WorldPosition? drawn = ReferenceEquals(other, client) ? world.Predictor.Position : Draws(client, entity);
            if (summary == null || drawn == null || Horizontal(drawn.Value, summary.Position) > ConvergedDistance)
            {
                return false;
            }
        }

        return true;
    }

    private static WorldPosition? Draws(SocketClient viewer, EntityId entity)
    {
        ClientWorld world = viewer.World;
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote)
            && remote.Buffer.TrySample(world.RemoteRenderTime, out WorldPosition position, out WorldDirection _)
                ? position
                : null;
    }

    private static PlayerSummary SummaryOf(IAdminCommandService admin, EntityId entity)
    {
        return admin.GetPlayers(AdminActor.LocalConsole).Single(player => player.Entity == entity);
    }

    private static float Horizontal(WorldPosition a, WorldPosition b)
    {
        float deltaX = a.X - b.X;
        float deltaZ = a.Z - b.Z;
        return (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static void AssertCleanTraffic(SocketClient client, string step)
    {
        Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
        Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
    }

    private static class Players
    {
        public const string FirstIdentity = "prototype-first";
        public const string FirstName = "ProtoFirst";
        public const string SecondIdentity = "prototype-second";
        public const string SecondName = "ProtoSecond";
    }

    [Test]
    public void TwoPlayers_OverRealSocketsAndPostgres_SeeEachOtherFightAndKeepTheirCharactersThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        Dictionary<string, PlayerSummary> stopped;
        using (IHost first = StartHost(root.Path))
        {
            stopped = PlayTogether(first, m_database.ConnectionString);
        }

        using (IHost restarted = StartHost(root.Path))
        {
            PlayAfterTheRestart(restarted, stopped);
            restarted.StopAsync().GetAwaiter().GetResult();
        }
    }
}
}
