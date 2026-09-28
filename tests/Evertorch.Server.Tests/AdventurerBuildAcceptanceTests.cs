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
///     the test's thread. A new Adventurer starts at job level 1, fights until its base level rises, and keeps what it
///     earned through a restart. Each later line of Milestone 9 adds its steps here: the job levels, the raised
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

        // A new character has learned nothing, so it lists no skill and cannot use Strike (Gameplay Systems §9).
        Assert.That(
            client.PumpUntil(() => client.World.SkillsReceivedAt > 0),
            Is.True,
            "enter: the skill list arrived");
        Assert.That(client.World.Skills, Is.Empty, "enter: nothing learned");
        var refused = new List<CommandRejected>();
        client.World.CommandRejectedReceived += refused.Add;
        uint sequence = client.Connection.SendUseSkill(new SkillDefinitionId("skill.strike"), default);
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

    // After a clean stop and a second server on the same database, the character keeps its level and experience, and
    // its job level is still 1.
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
        Assert.That(StoredJobProgress(), Is.EqualTo((1, 0L)), "restart: still job level 1");
        AssertCleanTraffic(client, "restart");
    }

    // The stored job level and job experience, once no learned skill is stored.
    private (int Level, long Experience) StoredJobProgress()
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT job_level, job_exp, (SELECT count(*) FROM character_skills WHERE character_id = characters.id) "
            + "FROM characters WHERE name = @name",
            connection);
        command.Parameters.AddWithValue("name", CharacterName);
        using NpgsqlDataReader reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True, "the character is stored");
        Assert.That(reader.GetInt64(2), Is.Zero, "no learned skill stored");
        return (reader.GetInt32(0), reader.GetInt64(1));
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
    }
}
}
