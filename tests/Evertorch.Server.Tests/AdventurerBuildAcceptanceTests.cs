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
///     The Milestone 9 Adventurer build (ROADMAP §8) end to end: the composed server host on a real PostgreSQL 18, real
///     UDP sockets on loopback, and a client built from the client's production networking and gameplay code, pumped on
///     the test's thread. A new Adventurer starts at job level 1, fights until its base level rises, spends its stat
///     point and its skill point, keeps what it earned and spent through a restart, has the Guildmaster return every
///     point, and keeps that through a second restart. Each later line of Milestone 9
///     adds its steps here:
///     the job levels, the raised
///     statistics, the learned skill levels and their measured effects, and the Guildmaster's reset.
/// </summary>
/// <remarks>
///     Combat and drops draw from scripted sources (<see cref="TestHosts.ScriptOutcomes" />): every attack hits
///     without a critical, and every entry of a drop table drops its smallest amount.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class AdventurerBuildAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingSlime = "monster.training_slime";
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";
    private const string Focus = "skill.focus";
    private const string Guildmaster = "npc.guildmaster";

    // Where the town's NPCs are in view, clear of every obstacle (as in TownLoopAcceptanceTests).
    private static readonly WorldPosition TownSpot = new(10f, 0f, -3f);
    private const string Identity = "adventurer";
    private const string CharacterName = "Builder1";
    private const float ConvergedDistance = 1e-3f;

    // More than the kills the second base level needs, in case a kill goes to a slime the player barely touched.
    private const int MaxKills = 6;

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
    ///     A new character enters at job level 1 with job experience 0, fights training slimes until its base level
    ///     rises, and the server stops. Returns what the server last showed of the character.
    /// </summary>
    private PlayerSummary PlayTheBuild(IHost host)
    {
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;

        using var client = new SocketClient(content, Identity, CharacterName);
        client.EnterWorld(port);
        Assert.That(client.Connection.Characters.Single().Name, Is.EqualTo(CharacterName), "enter: created");
        Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "enter: in town");
        Assert.That((client.World.Level, client.World.Experience), Is.EqualTo(((ushort)1, 0ul)), "enter: level 1");
        Assert.That(StoredJobProgress(), Is.EqualTo((1, 0L)), "enter: a new character is at job level 1");
        Assert.That(
            client.PumpUntil(() => client.World.Sheet != null),
            Is.True,
            "enter: the sheet arrived with the baseline");
        Assert.That(
            (client.World.Sheet!.JobLevel, client.World.Sheet.JobExperience,
                client.World.Sheet.JobExperienceToNextLevel),
            Is.EqualTo(((byte)1, 0ul, 30ul)),
            "enter: job level 1, 30 to the next");

        // A new character has learned nothing, so its tree lists every skill at level 0 and it cannot use Strike
        // (Gameplay Systems §9).
        Assert.That(
            client.PumpUntil(() => client.World.SkillsReceivedAt > 0),
            Is.True,
            "enter: the skill list arrived");
        Assert.That(
            client.World.Skills.Select(entry => (entry.Skill.Value, entry.Level)),
            Is.EqualTo(new[] { (Strike, (byte)0), (FirstAid, (byte)0), (Focus, (byte)0) }),
            "enter: the tree, nothing learned");
        var refused = new List<CommandRejected>();
        client.World.CommandRejectedReceived += refused.Add;
        uint sequence = client.Connection.SendUseSkill(new SkillDefinitionId(Strike), default);
        Assert.That(client.PumpUntil(() => refused.Count > 0), Is.True, "enter: Strike answered");
        Assert.That(
            (refused.Single().CommandSequence, refused.Single().Reason),
            Is.EqualTo((sequence, CommandRejectionReason.NotAllowedNow)),
            "enter: Strike not learned");

        FightUntilTheBaseLevelRises(client);
        Assert.That(
            client.PumpUntil(() => admin.GetPlayers(AdminActor.LocalConsole)
                .Any(player => player.Entity == client.World.LocalEntity && player.Level == client.World.Level)),
            Is.True,
            "fight: the console shows the level the client shows");
        PlayerSummary stopped = admin.GetPlayers(AdminActor.LocalConsole)
            .Single(player => player.Entity == client.World.LocalEntity);

        // Every kill gave as much job experience as base experience, so the job level kept pace (Gameplay Systems §2.1).
        CharacterSheet sheet = client.World.Sheet!;
        Assert.That(
            ((ushort)sheet.JobLevel, sheet.JobExperience),
            Is.EqualTo((client.World.Level, client.World.Experience)),
            "fight: the job level keeps pace with the base level");
        Assert.That(stopped.JobLevel, Is.EqualTo(sheet.JobLevel), "fight: the console shows the job level");
        SpendStatPoints(client);
        LearnStrike(client);

        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected),
            Is.True,
            "stop: the client heard the server stop");
        AssertCleanTraffic(client, "build");
        return stopped;
    }

    // Tab and the West button on one slime after another, until a kill raises the base level (Gameplay Systems §2.1).
    private static void FightUntilTheBaseLevelRises(SocketClient client)
    {
        const string step = "fight";
        ClientWorld world = client.World;
        var deaths = new List<EntityId>();
        world.EntityDiedReceived += death => deaths.Add(death.Entity);
        for (int kill = 0; kill < MaxKills && world.Level < 2; kill++)
        {
            EntityId slime = client.CycleTarget(true);
            Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
            Assert.That(world.Remotes[slime].DefinitionId, Is.EqualTo(TrainingSlime), step);
            Assert.That(client.PumpUntil(() => world.Target == slime), Is.True, $"{step}: the target confirmed");
            client.AttackTarget();
            Assert.That(
                client.PumpUntil(() => deaths.Contains(slime), FightLimit),
                Is.True,
                $"{step}: the slime died");
            Assert.That(world.IsLocalDead, Is.False, $"{step}: the player survived");
            client.PumpUntil(() => world.Level >= 2, TimeSpan.FromSeconds(1));
        }

        Assert.That(world.Level, Is.EqualTo((ushort)2), $"{step}: the base level rose");
    }

    // Base level 2 grants 3 stat points (Gameplay Systems §2): a raise of AGI costs 2, and one of DEX, 2 more, is
    // refused whole.
    private static void SpendStatPoints(SocketClient client)
    {
        const string step = "stats";
        ClientWorld world = client.World;
        Assert.That(world.Sheet!.StatPoints, Is.EqualTo((ushort)3), $"{step}: base level 2 grants 3 points");
        client.Connection.SendAllocateStat(PrimaryStat.Agi, 1);
        Assert.That(client.PumpUntil(() => world.Sheet!.Stats[1].Value == 6), Is.True, $"{step}: AGI raised");
        Assert.That(world.Sheet!.StatPoints, Is.EqualTo((ushort)1), $"{step}: 2 points spent");

        var refused = new List<CommandRejected>();
        world.CommandRejectedReceived += refused.Add;
        uint sequence = client.Connection.SendAllocateStat(PrimaryStat.Dex, 1);
        Assert.That(client.PumpUntil(() => refused.Count > 0), Is.True, $"{step}: DEX answered");
        Assert.That(
            (refused.Single().CommandSequence, refused.Single().Reason),
            Is.EqualTo((sequence, CommandRejectionReason.NotEnoughPoints)),
            $"{step}: DEX needs 2 points");
        Assert.That(world.Sheet!.Stats[4].Value, Is.EqualTo((byte)5), $"{step}: DEX unchanged");
    }

    // Job level 2 grants 1 skill point (Gameplay Systems §9): Focus, whose prerequisite Strike 1 is not met, is refused
    // with 13 before the point is looked at; Strike takes the point; then Focus is refused with 12.
    private static void LearnStrike(SocketClient client)
    {
        const string step = "skills";
        ClientWorld world = client.World;
        Assert.That(world.Sheet!.SkillPoints, Is.EqualTo((byte)1), $"{step}: job level 2 grants 1 point");
        var refused = new List<CommandRejected>();
        world.CommandRejectedReceived += refused.Add;

        uint early = client.Connection.SendLearnSkill(new SkillDefinitionId(Focus));
        Assert.That(client.PumpUntil(() => refused.Count > 0), Is.True, $"{step}: Focus answered");
        uint learned = client.Connection.SendLearnSkill(new SkillDefinitionId(Strike));
        Assert.That(
            client.PumpUntil(() =>
                world.Skills.Count > 0 && world.Skills[0].Level == 1 && world.Sheet!.SkillPoints == 0),
            Is.True,
            $"{step}: Strike learned");
        uint late = client.Connection.SendLearnSkill(new SkillDefinitionId(Focus));
        Assert.That(client.PumpUntil(() => refused.Count > 1), Is.True, $"{step}: Focus answered again");

        Assert.That(learned, Is.GreaterThan(early));
        Assert.That(
            refused.Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(
                new[]
                {
                    (early, CommandRejectionReason.RequirementNotMet), (late, CommandRejectionReason.NotEnoughPoints)
                }),
            $"{step}: the prerequisite, then the point");
        Assert.That(world.Skills.Select(entry => entry.Level), Is.EqualTo(new byte[] { 1, 0, 0 }), step);
    }

    // After a clean stop and a second server on the same database, the character keeps its level and experience, its
    // job level and job experience, the statistic it raised, and the skill it learned.
    private void PlayAfterTheRestart(IHost host, PlayerSummary stopped)
    {
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var client = new SocketClient(content, Identity, CharacterName);
        client.EnterWorld(port);

        Assert.That(
            client.DistanceTo(stopped.Position),
            Is.LessThanOrEqualTo(ConvergedDistance),
            "restart: where the shutdown checkpoint left it");
        Assert.That(
            (client.World.Level, client.World.Experience),
            Is.EqualTo(((ushort)stopped.Level, (ulong)stopped.Experience)),
            "restart: the level and experience the fight gave");
        Assert.That(
            client.PumpUntil(() => admin.GetPlayers(AdminActor.LocalConsole).Any(player =>
                player.Entity == client.World.LocalEntity
                && player.Level == stopped.Level
                && player.Experience == stopped.Experience)),
            Is.True,
            "restart: the server kept them");
        Assert.That(
            StoredJobProgress(),
            Is.EqualTo((stopped.JobLevel, stopped.Experience)),
            "restart: the job level and job experience the fight gave, stored");
        Assert.That(
            client.PumpUntil(() => client.World.Sheet != null),
            Is.True,
            "restart: the sheet arrived with the baseline");
        Assert.That(
            ((int)client.World.Sheet!.JobLevel, (long)client.World.Sheet.JobExperience),
            Is.EqualTo((stopped.JobLevel, stopped.Experience)),
            "restart: the client shows them");
        Assert.That(
            (client.World.Sheet.Stats[1].Value, client.World.Sheet.StatPoints),
            Is.EqualTo(((byte)6, (ushort)1)),
            "restart: the raised AGI and the point left");
        Assert.That(StoredAgility(), Is.EqualTo(6), "restart: the raised AGI, stored");
        Assert.That(
            client.PumpUntil(() => client.World.SkillsReceivedAt > 0),
            Is.True,
            "restart: the skill list arrived");
        Assert.That(
            client.World.Skills.Select(entry => (entry.Skill.Value, entry.Level)),
            Is.EqualTo(new[] { (Strike, (byte)1), (FirstAid, (byte)0), (Focus, (byte)0) }),
            "restart: Strike 1 kept");
        Assert.That(
            (client.World.Sheet.SkillPoints, StoredSkills()),
            Is.EqualTo(((byte)0, $"{Strike}=1")),
            "restart: the point spent, and the level stored");
        ResetAtTheGuildmaster(client);
        AssertCleanTraffic(client, "restart");
    }

    // The Guildmaster returns every point for free (Gameplay Systems §6.1): AGI back to 5, Strike to 0, the 3 stat
    // points and the skill point left again; its checkpoint is queued at once, so the database has it at once.
    private void ResetAtTheGuildmaster(SocketClient client)
    {
        const string step = "reset";
        ClientWorld world = client.World;
        Assert.That(client.Controller.TryMoveTo(world.Predictor.Position, TownSpot), Is.True, $"{step}: a way to town");
        Assert.That(
            client.PumpUntil(() => !client.Controller.HasPath && client.DistanceTo(TownSpot) < 0.5f),
            Is.True,
            $"{step}: in town");
        Assert.That(
            client.PumpUntil(() => GuildmasterInView(world) != null),
            Is.True,
            $"{step}: the Guildmaster in view, offering the reset");
        RemoteEntity guildmaster = GuildmasterInView(world)!;
        int windows = client.NpcWindows.Count;
        client.TalkTo(guildmaster.Entity);
        Assert.That(client.PumpUntil(() => client.NpcWindows.Count > windows), Is.True, $"{step}: walked up to it");

        client.Connection.SendResetBuild(guildmaster.Entity);
        Assert.That(
            client.PumpUntil(() => world.Sheet!.StatPoints == 3
                && world.Sheet.SkillPoints == 1
                && world.Skills.All(entry => entry.Level == 0)),
            Is.True,
            $"{step}: every point back; {world.LastRejection}");
        Assert.That(world.Sheet!.Stats[1].Value, Is.EqualTo((byte)5), $"{step}: AGI back to the job's start");
        Assert.That(
            client.PumpUntil(() => StoredAgility() == 5 && StoredSkills().Length == 0),
            Is.True,
            $"{step}: checkpointed at once");
    }

    private static RemoteEntity? GuildmasterInView(ClientWorld world)
    {
        RemoteEntity? remote = world.Remotes.Values.SingleOrDefault(candidate =>
            candidate.Kind == EntityKind.Npc && candidate.DefinitionId == Guildmaster);
        return remote != null
            && world.TryGetNpcServices(remote.Entity, out NpcServices? services)
            && services!.OffersReset
                ? remote
                : null;
    }

    // After the second restart the character still has every point back and nothing learned.
    private void PlayAfterTheReset(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var client = new SocketClient(content, Identity, CharacterName);
        client.EnterWorld(port);

        Assert.That(
            client.PumpUntil(() => client.World.Sheet != null && client.World.SkillsReceivedAt > 0),
            Is.True,
            "second restart: the sheet and the list arrived");
        CharacterSheet sheet = client.World.Sheet!;
        Assert.That(
            (sheet.Stats[1].Value, sheet.StatPoints, sheet.SkillPoints),
            Is.EqualTo(((byte)5, (ushort)3, (byte)1)),
            "second restart: the reset kept");
        Assert.That(client.World.Skills.Select(entry => entry.Level), Is.All.EqualTo((byte)0));
        AssertCleanTraffic(client, "second restart");
    }

    private (int Level, long Experience) StoredJobProgress()
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT job_level, job_exp FROM characters WHERE name = @name",
            connection);
        command.Parameters.AddWithValue("name", CharacterName);
        using NpgsqlDataReader reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True, "the character is stored");
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    // "skill=level" for each stored skill, in the skills' order.
    private string StoredSkills()
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT string_agg(s.skill_definition_id || '=' || s.level, ',' ORDER BY s.skill_definition_id) "
            + "FROM character_skills s JOIN characters c ON c.id = s.character_id WHERE c.name = @name",
            connection);
        command.Parameters.AddWithValue("name", CharacterName);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private int StoredAgility()
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand("SELECT agi FROM characters WHERE name = @name", connection);
        command.Parameters.AddWithValue("name", CharacterName);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void AssertCleanTraffic(SocketClient client, string step)
    {
        Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
        Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
    }

    [Test]
    public void Build_OverRealSocketsAndPostgres_StartsAtJobLevelOneAndKeepsWhatItEarnedThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        PlayerSummary stopped;
        using (IHost first = StartHost(root.Path))
        {
            stopped = PlayTheBuild(first);
        }

        using (IHost restarted = StartHost(root.Path))
        {
            PlayAfterTheRestart(restarted, stopped);
            restarted.StopAsync().GetAwaiter().GetResult();
        }

        using (IHost reset = StartHost(root.Path))
        {
            PlayAfterTheReset(reset);
            reset.StopAsync().GetAwaiter().GetResult();
        }
    }
}
}
