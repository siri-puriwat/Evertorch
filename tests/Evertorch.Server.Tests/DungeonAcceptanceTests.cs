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
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 13 "A dungeon and its boss" exit criterion's server side end to end (ROADMAP §8): the composed
///     server host on a real PostgreSQL 18 and real UDP sockets on loopback, loading the server-only test package
///     (<see cref="DungeonPackage" />), with three clients built from the client's production networking and gameplay
///     code. Three players enter the training ground together, Aldo invites Bree and Cora into a party, and the party
///     walks through the ground's portal onto the training field, then through the field's east portal into the Umbral
///     Grotto, where a pack of Grotto Crawlers links when Aldo attacks one, a Gloom Wisp numbs Bree, and in its chamber
///     the Slime Monarch slams: Cora steps out of the telegraph and is spared while Aldo is struck, the party brings the
///     boss down, and each member hears it fall and, brought back by the console, appear. Each later line of Milestone 13
///     adds its steps here: the most valuable player's prize, the quests, and a restart.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class DungeonAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingField = "map.training_field";
    private const string UmbralGrotto = "map.umbral_grotto";
    private const string GrottoCrawler = "monster.grotto_crawler";
    private const string GloomWisp = "monster.gloom_wisp";
    private const string SlimeMonarch = "monster.slime_monarch";
    private const string AldoIdentity = "dungeon-aldo";
    private const string AldoName = "Aldo";
    private const string BreeIdentity = "dungeon-bree";
    private const string BreeName = "Bree";
    private const string CoraIdentity = "dungeon-cora";
    private const string CoraName = "Cora";

    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WalkLimit = TimeSpan.FromSeconds(30);
    private static readonly StatusDefinitionId Numbed = new("status.numbed");
    private static readonly SkillDefinitionId QuakeSlam = new("skill.quake_slam");

    // Aldo 2.5 m west of the boss's home at (18, 0, 18); Bree and Cora 9 m from it, beyond its 6 m of notice; and Cora's
    // place inside the slam's 4 m.
    private static readonly WorldPosition BeforeTheBoss = new(15.5f, 0f, 18f);
    private static readonly WorldPosition HangingBack = new(9f, 0f, 17f);
    private static readonly WorldPosition WaitingBack = new(9f, 0f, 19f);
    private static readonly WorldPosition BesideTheBoss = new(16f, 0f, 20f);

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
            new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=13" },
            contentRootPath,
            m_database.ConnectionString);
        IHost host = builder.Build();
        host.Start();
        return host;
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
        Assert.That(isEntered, Is.True, $"enter: all three entered ({states})");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "enter: in town");
        }
    }

    // Aldo invites Bree, then Cora; once both accept, each of the three hears the roster of three that Aldo leads
    // (Gameplay Systems §14).
    private static void FormParty(SocketClient aldo, SocketClient bree, SocketClient cora)
    {
        const string step = "party";
        SocketClient[] all = { aldo, bree, cora };
        var heard = new Dictionary<SocketClient, List<string>>();
        var rosters = new Dictionary<SocketClient, PartyRoster>();
        foreach (SocketClient client in all)
        {
            var events = new List<string>();
            heard.Add(client, events);
            client.Connection.PartyEventReceived += partyEvent => events.Add($"{partyEvent.Kind} {partyEvent.Name}");
            client.Connection.PartyRosterReceived += roster => rosters[client] = roster;
        }

        foreach ((SocketClient invitee, string name) in new[] { (bree, BreeName), (cora, CoraName) })
        {
            aldo.Connection.SendPartyInvite(name);
            Assert.That(
                SocketClients.PumpUntil(() => heard[invitee].Contains($"Invited {AldoName}"), all),
                Is.True,
                $"{step}: {name} invited");
            invitee.Connection.SendPartyReply(AldoName, true);
            Assert.That(
                SocketClients.PumpUntil(() => heard[aldo].Contains($"Joined {name}"), all),
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
        foreach (SocketClient client in all)
        {
            Assert.That(rosters[client].LeaderIndex, Is.Zero, $"{step}: Aldo leads");
        }
    }

    // The party walks into the portal before the ground's east gate together, and each follows its character to the
    // field (Gameplay Systems §4.2).
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
                party),
            Is.True,
            $"{step}: every member followed its character to the field");
    }

    // The party walks on into the portal before the field's east gate, and each follows its character into the grotto,
    // where it stands by the grotto's west gate (Gameplay Systems §4.2).
    private static void CrossIntoTheGrotto(ServerContent content, params SocketClient[] party)
    {
        const string step = "into the grotto";
        var grotto = new MapDefinitionId(UmbralGrotto);
        MapPortal portal = content.Maps[new MapDefinitionId(TrainingField)].Portals
            .Single(candidate => candidate.DestinationMap == grotto);
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
                () => party.All(client => client.Connection.World?.Map == grotto
                    && client.Connection.World.Inventory.IsCurrent),
                party),
            Is.True,
            $"{step}: every member followed its character into the grotto");
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.World.Predictor.Position,
                Is.EqualTo(content.Maps[grotto].SpawnPosition),
                $"{step}: by the west gate");
        }
    }

    private static bool IsGrottoCrawler(ClientWorld world, EntityId entity)
    {
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote) && remote.DefinitionId == GrottoCrawler;
    }

    // The party walks up the corridor to the crawler hall, and Aldo attacks the nearest Grotto Crawler: it strikes
    // back, and its kin within 11 m that see it answer its call, so more than one crawler swings at the party
    // (Gameplay Systems §10). The test package disarms the crawlers, so the party lives on, and takes their dodge away,
    // which no level-1 character's hit could beat.
    private static void MeetThePackThatLinks(SocketClient aldo, params SocketClient[] party)
    {
        const string step = "the pack";
        var hall = new WorldPosition(-22f, 0f, 4f);
        var swingers = new HashSet<EntityId>();
        EntityId[] members = party.Select(client => client.World.LocalEntity).ToArray();
        foreach (SocketClient client in party)
        {
            ClientWorld world = client.World;
            world.AttackStartedReceived += started =>
            {
                if (members.Contains(started.Target) && IsGrottoCrawler(world, started.Attacker))
                {
                    swingers.Add(started.Attacker);
                }
            };
            Assert.That(
                client.Controller.TryMoveTo(world.Predictor.Position, hall),
                Is.True,
                $"{step}: a way to the crawler hall");
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => !client.Controller.HasPath), party),
            Is.True,
            $"{step}: in the crawler hall");
        EntityId crawler = aldo.CycleTarget(true);
        Assert.That(IsGrottoCrawler(aldo.World, crawler), Is.True, $"{step}: Tab found a Grotto Crawler");
        Assert.That(SocketClients.PumpUntil(() => aldo.World.Target == crawler, party), Is.True, $"{step}: targeted");
        int hits = 0;
        aldo.World.DamageReceived += damage =>
            hits += damage.Source == aldo.World.LocalEntity && damage.Result != CombatResult.Miss ? 1 : 0;
        aldo.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(() => swingers.Count >= 2, FightLimit, party),
            Is.True,
            $"{step}: more than one crawler swung at the party ({swingers.Count}, after {hits} hits by Aldo)");
        aldo.AutoAttack.OnWalkRequested();
    }

    // The party walks back down the corridor and east into the wisp gallery, and Bree attacks a Gloom Wisp: it keeps
    // its distance and casts Numbing Spark at Bree, whose client hears of the numbing and whose sheet's attack speed
    // and flee fall (Gameplay Systems §9.1). The test package disarms the wisps, takes their dodge away, and has them
    // cast at every decision they may.
    private static void MeetTheWispThatNumbs(SocketClient bree, params SocketClient[] party)
    {
        const string step = "the wisp";
        var gallery = new WorldPosition(6f, 0f, -20f);
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.Controller.TryMoveTo(client.World.Predictor.Position, gallery),
                Is.True,
                $"{step}: a way to the wisp gallery");
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => !client.Controller.HasPath), WalkLimit, party),
            Is.True,
            $"{step}: in the wisp gallery");
        CharacterSheet before = bree.World.Sheet!;
        EntityId wisp = default;
        for (int tries = 0; tries < 8 && !IsGloomWisp(bree.World, wisp); tries++)
        {
            EntityId next = bree.CycleTarget(true);
            SocketClients.PumpUntil(() => bree.World.Target == next, party);
            wisp = bree.World.Target;
        }

        Assert.That(IsGloomWisp(bree.World, wisp), Is.True, $"{step}: Tab found a Gloom Wisp");
        // A hit provokes the wisp, and a miss does not; Bree then stands, so it keeps its distance and casts rather
        // than walking away.
        bool hasHit = false;
        bree.World.DamageReceived += damage =>
            hasHit |= damage.Source == bree.World.LocalEntity
                && damage.Target == wisp
                && damage.Result != CombatResult.Miss;
        int misses = 0;
        bree.World.DamageReceived += damage =>
            misses += damage.Source == bree.World.LocalEntity && damage.Result == CombatResult.Miss ? 1 : 0;
        bree.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(() => hasHit, FightLimit, party),
            Is.True,
            $"{step}: Bree hit the wisp (after {misses} misses)");
        bree.AutoAttack.OnWalkRequested();
        Assert.That(
            SocketClients.PumpUntil(
                () => bree.World.StatusEffects.Any(effect => effect.Status == Numbed),
                FightLimit,
                party),
            Is.True,
            $"{step}: Bree heard of the numbing");
        Assert.That(
            SocketClients.PumpUntil(
                () => bree.World.Sheet!.AttackSpeed < before.AttackSpeed && bree.World.Sheet.Flee < before.Flee,
                party),
            Is.True,
            $"{step}: Bree's attacks and dodge slowed");
    }

    private static bool IsAll(Func<SocketClient, bool> condition, params SocketClient[] clients)
    {
        return clients.All(condition);
    }

    // The party walks back west and up into the corridor, then east into the boss chamber. Aldo goes in first, and the
    // Slime Monarch takes Aldo for its target; then Cora comes beside it, and once the client sees its slam's cast
    // begin, Cora walks out of the 4 m and is spared while Aldo, who stays, is struck (Gameplay Systems §9). The party
    // brings the boss down and each member hears it fall; the console brings it back and each hears it appear (Network
    // Protocol §9). The test package leaves the boss 60 HP and no dodge, has its swings and its slam strike for 1, and
    // has it slam whenever it may.
    private static void MeetTheSlimeMonarch(
        IAdminCommandService admin,
        SocketClient aldo,
        SocketClient bree,
        SocketClient cora)
    {
        const string step = "the boss";
        SocketClient[] party = { aldo, bree, cora };
        var heard = new Dictionary<SocketClient, List<string>>();
        foreach (SocketClient client in party)
        {
            var lines = new List<string>();
            heard.Add(client, lines);
            client.Connection.BossAnnouncementReceived += announcement =>
                lines.Add($"{announcement.Kind} {announcement.Monster.Value}");
        }

        foreach ((SocketClient client, WorldPosition place) in new[]
                 {
                     (aldo, BeforeTheBoss), (bree, HangingBack), (cora, WaitingBack)
                 })
        {
            Assert.That(
                client.Controller.TryMoveTo(client.World.Predictor.Position, place),
                Is.True,
                $"{step}: a way into the chamber");
        }

        Assert.That(
            SocketClients.PumpUntil(() => IsAll(client => !client.Controller.HasPath, party), WalkLimit, party),
            Is.True,
            $"{step}: in the chamber");
        EntityId boss = aldo.World.Remotes.Values.Single(remote => remote.DefinitionId == SlimeMonarch).Entity;
        bool isAldoFought = false;
        aldo.World.AttackStartedReceived += started =>
            isAldoFought |= started.Attacker == boss && started.Target == aldo.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => isAldoFought, FightLimit, party),
            Is.True,
            $"{step}: the boss swung at Aldo");
        Assert.That(
            cora.Controller.TryMoveTo(cora.World.Predictor.Position, BesideTheBoss),
            Is.True,
            $"{step}: a way beside the boss");
        Assert.That(
            SocketClients.PumpUntil(() => !cora.Controller.HasPath, party),
            Is.True,
            $"{step}: Cora beside the boss");

        bool hasSlamBegun = false;
        bool hasSteppedOut = false;
        bool isAldoStruck = false;
        bool isCoraStruck = false;
        cora.World.SkillCastStartedReceived += started =>
            hasSlamBegun |= started.Caster == boss && started.Skill == QuakeSlam;
        aldo.World.SkillResolvedReceived += resolved =>
            isAldoStruck |= resolved.Skill == QuakeSlam && resolved.Target == aldo.World.LocalEntity;
        cora.World.SkillResolvedReceived += resolved =>
            isCoraStruck |= resolved.Skill == QuakeSlam && resolved.Target == cora.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(
                () =>
                {
                    if (hasSlamBegun && !hasSteppedOut)
                    {
                        hasSteppedOut = cora.Controller.TryMoveTo(cora.World.Predictor.Position, WaitingBack);
                    }

                    return hasSteppedOut && isAldoStruck;
                },
                FightLimit,
                party),
            Is.True,
            $"{step}: a slam began, Cora stepped out, and it struck Aldo");
        SocketClients.PumpUntil(() => false, TimeSpan.FromSeconds(0.5), party);
        Assert.That(isCoraStruck, Is.False, $"{step}: the slam spared Cora");

        foreach (SocketClient client in party)
        {
            client.Connection.SendTarget(boss);
        }

        Assert.That(
            SocketClients.PumpUntil(() => IsAll(client => client.World.Target == boss, party), party),
            Is.True,
            $"{step}: every member targeted the boss");
        foreach (SocketClient client in party)
        {
            client.AttackTarget();
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => IsAll(client => heard[client].Contains($"Fell {SlimeMonarch}"), party),
                FightLimit,
                party),
            Is.True,
            $"{step}: every member heard the boss fall");

        IReadOnlyList<MonsterDefinitionId> returned = admin
            .RespawnBossesAsync(new AdminActor("acceptance", "test"))
            .GetAwaiter()
            .GetResult();
        Assert.That(returned, Is.EqualTo(new[] { new MonsterDefinitionId(SlimeMonarch) }), $"{step}: brought back");
        Assert.That(
            SocketClients.PumpUntil(
                () => IsAll(client => heard[client].Contains($"Appeared {SlimeMonarch}"), party),
                party),
            Is.True,
            $"{step}: every member heard the boss appear");
    }

    private static bool IsGloomWisp(ClientWorld world, EntityId entity)
    {
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote) && remote.DefinitionId == GloomWisp;
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
    public void AParty_OverRealSocketsAndPostgres_CrossesTheFieldIntoTheGrotto()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            DungeonPackage.Build(
                DungeonPackage.Disarm(GrottoCrawler),
                DungeonPackage.Expose(GrottoCrawler),
                DungeonPackage.Disarm(GloomWisp),
                DungeonPackage.Expose(GloomWisp),
                DungeonPackage.AlwaysCast(GloomWisp),
                DungeonPackage.Weaken(SlimeMonarch, 60),
                DungeonPackage.Disarm(SlimeMonarch),
                DungeonPackage.Expose(SlimeMonarch),
                DungeonPackage.Muffle(SlimeMonarch),
                DungeonPackage.AlwaysCast(SlimeMonarch)));

        using IHost host = StartHost(root.Path);
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var aldo = new SocketClient(content, AldoIdentity, AldoName);
        using var bree = new SocketClient(content, BreeIdentity, BreeName);
        using var cora = new SocketClient(content, CoraIdentity, CoraName);
        EnterTogether(port, aldo, bree, cora);
        AssertCleanTraffic("enter", aldo, bree, cora);

        FormParty(aldo, bree, cora);
        AssertCleanTraffic("party", aldo, bree, cora);

        CrossToTheField(content, aldo, bree, cora);
        AssertCleanTraffic("to the field", aldo, bree, cora);

        CrossIntoTheGrotto(content, aldo, bree, cora);
        AssertCleanTraffic("into the grotto", aldo, bree, cora);

        MeetThePackThatLinks(aldo, aldo, bree, cora);
        AssertCleanTraffic("the pack", aldo, bree, cora);

        MeetTheWispThatNumbs(bree, aldo, bree, cora);
        AssertCleanTraffic("the wisp", aldo, bree, cora);

        MeetTheSlimeMonarch(host.Services.GetRequiredService<IAdminCommandService>(), aldo, bree, cora);
        AssertCleanTraffic("the boss", aldo, bree, cora);

        host.StopAsync().GetAwaiter().GetResult();
    }
}
}
