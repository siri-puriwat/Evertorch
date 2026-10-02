using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A share that covers the next level recalculates the statistics and restores HP and SP in full (Gameplay
///     Systems §2, §2.1). The adventurer's table needs 30 from level 1 and caps at level 25.
/// </summary>
[TestFixture]
public sealed class LevelUpTests
{
    private static DerivedStats AdventurerAt(int level)
    {
        return new RenewalCharacterRules().CalculateDerivedStats(
            new CharacterBuild(level, new PrimaryStats(5, 5, 5, 5, 5, 5), 60, 8, 20, 3, 44, 5f));
    }

    private static (TestServer Server, ConnectionId Player, MonsterEntity Slime) Arrange()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId player = server.EnterWorld(1);
        MonsterEntity slime = server.MonstersNear(server.World.Maps.Single().Definition.SpawnPosition).First();
        slime.LogDamage(server.PlayerOf(player).Character, 50);
        return (server, player, slime);
    }

    private static void Kill(TestServer server, WorldEntity entity)
    {
        server.Combat.Kill(server.World.Maps.Single(), entity, null, server.CurrentTick);
    }

    [Test]
    public void ApplyStats_KeepsHpAndSpWithinTheNewMaximums()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.Experience = 25;
        Kill(server, slime);
        Assert.That(player.CurrentHealth, Is.EqualTo(AdventurerAt(2).MaxHp));

        player.Level = 1;
        new CharacterStats(new RenewalCharacterRules()).Recalculate(
            player,
            server.Content.Jobs[player.Job]);

        Assert.That(player.MaxHealth, Is.EqualTo(AdventurerAt(1).MaxHp));
        Assert.That(player.CurrentHealth, Is.EqualTo(AdventurerAt(1).MaxHp));
        Assert.That(player.CurrentSpirit, Is.EqualTo(AdventurerAt(1).MaxSp));
    }

    // The cap is the table's: past level 15 a character levels on toward 25 (Gameplay Systems §2.1).
    [Test]
    public void Kill_AtLevel15_LevelsOnTowardTheCapOf25()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.Level = 15;
        player.Experience = 1215;

        Kill(server, slime);

        Assert.That((player.Level, player.Experience), Is.EqualTo((16, 5L)), "1,220 to level 16");
    }

    [Test]
    public void Kill_AtTheCap_KeepsTheLevelAndNoExperience()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.Level = 25;
        server.Transport.ClearSent();

        Kill(server, slime);

        Assert.That(player.Level, Is.EqualTo(25));
        Assert.That(player.Experience, Is.Zero);
        Assert.That(
            server.Transport.ControlOpcodesSentTo(connection),
            Has.None.EqualTo(MessageOpcode.CharacterHealth));
        CharacterProgress progress = server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CharacterProgress)
            .Select(message => CharacterProgress.TryRead(message.Payload, out CharacterProgress read) ? read : default)
            .Single();
        Assert.That(progress.ExperienceToNextLevel, Is.Zero, "nothing more at the cap");
        Assert.That(server.ProgressionLog.Entries, Is.Empty);
    }

    [Test]
    public void Kill_WhenTheCharacterIsRetained_LevelsItUpWithoutSendingAnything()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false, reconnectGraceMs: 60_000);
        ConnectionId connection = server.EnterWorld(1);
        MonsterEntity slime = server.MonstersNear(server.World.Maps.Single().Definition.SpawnPosition).First();
        PlayerEntity player = server.PlayerOf(connection);
        player.Experience = 25;
        slime.LogDamage(player.Character, 50);
        server.Disconnect(connection);
        server.Tick();
        server.Transport.ClearSent();

        Kill(server, slime);

        Assert.That(player.Level, Is.EqualTo(2));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
        Assert.That(server.ProgressionLog.Entries.Single().Fields["Connection"], Is.EqualTo(0L));
    }

    [Test]
    public void Kill_WhenTheCharacterLevelsUp_LogsTheLevelUp()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        server.PlayerOf(connection).Experience = 25;

        Kill(server, slime);

        (LogLevel level, EventId eventId, _, IReadOnlyDictionary<string, object?> fields) =
            server.ProgressionLog.Entries.Single();
        Assert.That(eventId.Id, Is.EqualTo(1006));
        Assert.That(eventId.Name, Is.EqualTo("CharacterLeveledUp"));
        Assert.That(level, Is.EqualTo(LogLevel.Information));
        Assert.That(fields["Character"], Is.EqualTo(1L));
        Assert.That(fields["Connection"], Is.EqualTo(connection.Value));
        Assert.That(fields["Level"], Is.EqualTo(2));
        Assert.That(fields["PreviousLevel"], Is.EqualTo(1));
    }

    [Test]
    public void Kill_WhenTheCharacterLevelsUp_QueuesACheckpointOfTheNewLevel()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.Experience = 25;
        int checkpoints = server.Store.Checkpoints.Count;

        Kill(server, slime);
        server.Tick();

        CharacterCheckpoint checkpoint = server.Store.Checkpoints.Skip(checkpoints).Single();
        Assert.That(checkpoint.Level, Is.EqualTo(2));
        Assert.That(checkpoint.Experience, Is.EqualTo(5));
        Assert.That(checkpoint.Health, Is.EqualTo(player.MaxHealth));
        Assert.That(checkpoint.Spirit, Is.EqualTo(player.MaxSpirit));
    }

    [Test]
    public void Kill_WhenTheShareCoversTheNextLevel_RecalculatesAndRestoresHpAndSp()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.Experience = 25;
        player.CurrentHealth = 10;
        player.CurrentSpirit = 2;
        server.Transport.ClearSent();

        Kill(server, slime);

        DerivedStats levelTwo = AdventurerAt(2);
        Assert.That(player.Level, Is.EqualTo(2));
        Assert.That(player.Experience, Is.EqualTo(5));
        Assert.That(player.Stats.MaxHp, Is.EqualTo(levelTwo.MaxHp));
        Assert.That(player.Stats.PhysicalAttack, Is.EqualTo(levelTwo.PhysicalAttack));
        Assert.That(player.MaxHealth, Is.EqualTo(levelTwo.MaxHp));
        Assert.That(player.MaxSpirit, Is.EqualTo(levelTwo.MaxSp));
        Assert.That(player.CurrentHealth, Is.EqualTo(levelTwo.MaxHp));
        Assert.That(player.CurrentSpirit, Is.EqualTo(levelTwo.MaxSp));
        Assert.That(levelTwo.MaxHp, Is.GreaterThan(AdventurerAt(1).MaxHp), "the level-up raised the maximum");

        CharacterHealth health = server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CharacterHealth)
            .Select(message => CharacterHealth.TryRead(message.Payload, out CharacterHealth read) ? read : default)
            .Single();
        Assert.That(health.Current, Is.EqualTo((uint)levelTwo.MaxHp));
        Assert.That(health.Maximum, Is.EqualTo((uint)levelTwo.MaxHp));
        Assert.That(health.CurrentSpirit, Is.EqualTo((uint)levelTwo.MaxSp));
        Assert.That(health.MaximumSpirit, Is.EqualTo((uint)levelTwo.MaxSp));

        CharacterProgress progress = server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CharacterProgress)
            .Select(message => CharacterProgress.TryRead(message.Payload, out CharacterProgress read) ? read : default)
            .Single();
        Assert.That(progress.Level, Is.EqualTo(2));
        Assert.That(progress.Experience, Is.EqualTo(5UL));
        Assert.That(progress.ExperienceToNextLevel, Is.EqualTo(50UL));
        MessageOpcode[] order = server.Transport.ControlOpcodesSentTo(connection)
            .Where(opcode => opcode is MessageOpcode.EntityDied
                or MessageOpcode.CharacterHealth
                or MessageOpcode.CharacterProgress)
            .ToArray();
        Assert.That(
            order,
            Is.EqualTo(new[]
                { MessageOpcode.EntityDied, MessageOpcode.CharacterHealth, MessageOpcode.CharacterProgress }),
            "the death, then HP and SP, then the progress");
    }

    [Test]
    public void Kill_WhenTheShareStaysBelowTheNextLevel_QueuesNoCheckpoint()
    {
        (TestServer server, ConnectionId _, MonsterEntity slime) = Arrange();
        int checkpoints = server.Store.Checkpoints.Count;

        Kill(server, slime);
        server.Tick();

        Assert.That(server.Store.Checkpoints, Has.Count.EqualTo(checkpoints));
    }
}
}
