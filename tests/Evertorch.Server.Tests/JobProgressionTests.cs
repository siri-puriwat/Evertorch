using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Job experience and job levels (Gameplay Systems §2.1, §2.2) and the owner's <see cref="CharacterSheet" />
///     (Network Protocol §9). The Adventurer's job table needs 30 from job level 1 and caps at job level 10; a training
///     slime gives 10 job experience.
/// </summary>
[TestFixture]
public sealed class JobProgressionTests
{
    private static (TestServer Server, ConnectionId Player, MonsterEntity Slime) Arrange()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false, withAdventurerBuild: false);
        ConnectionId player = server.EnterWorld(1);
        MonsterEntity slime = server.MonstersNear(server.World.Maps.Single().Definition.SpawnPosition).First();
        slime.LogDamage(server.PlayerOf(player).Character, 50);
        return (server, player, slime);
    }

    private static void Kill(TestServer server, WorldEntity entity)
    {
        server.Combat.Kill(server.World.Maps.Single(), entity, null, server.CurrentTick);
    }

    private static List<CharacterSheet> Sheets(TestServer server, ConnectionId player)
    {
        var sheets = new List<CharacterSheet>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == MessageOpcode.CharacterSheet
                && CharacterSheet.TryRead(message.Payload, out CharacterSheet? sheet))
            {
                sheets.Add(sheet!);
            }
        }

        return sheets;
    }

    [Test]
    public void Kill_AtTheJobCap_KeepsTheJobLevelAndNoJobExperience()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.JobLevel = 10;

        Kill(server, slime);
        server.Tick();

        Assert.That((player.JobLevel, player.JobExperience), Is.EqualTo((10, 0L)));
        Assert.That(Sheets(server, connection).Last().JobExperienceToNextLevel, Is.Zero, "0 at the cap");
    }

    [Test]
    public void Kill_GivesItsJobExperienceBySharedDamage_AndTheSheetFollowsInTheTick()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        server.Transport.ClearSent();

        Kill(server, slime);
        server.Tick();

        Assert.That((player.JobLevel, player.JobExperience), Is.EqualTo((1, 10L)));
        CharacterSheet sheet = Sheets(server, connection).Single();
        Assert.That(
            (sheet.JobLevel, sheet.JobExperience, sheet.JobExperienceToNextLevel),
            Is.EqualTo(((byte)1, 10ul, 30ul)));
    }

    // A dead character shares nothing, whether base or job experience (Gameplay Systems §2.1).
    [Test]
    public void Kill_OfAMonsterADeadCharacterHit_GivesItNoJobExperience()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentHealth = 0;
        server.Combat.Kill(server.World.Maps.Single(), player, null, server.CurrentTick);

        Kill(server, slime);

        Assert.That(player.JobExperience, Is.Zero);
    }

    [Test]
    public void Kill_WhenTheJobShareCoversTheNextJobLevel_LevelsTheJobLogsItAndQueuesACheckpoint()
    {
        (TestServer server, ConnectionId connection, MonsterEntity slime) = Arrange();
        PlayerEntity player = server.PlayerOf(connection);
        player.JobExperience = 25;
        int checkpoints = server.Store.Checkpoints.Count;
        int maxHealth = player.MaxHealth;
        player.CurrentHealth = 1;

        Kill(server, slime);
        server.Tick();

        Assert.That((player.JobLevel, player.JobExperience), Is.EqualTo((2, 5L)));
        Assert.That(player.CurrentHealth, Is.EqualTo(1), "a job level restores nothing");
        Assert.That(player.MaxHealth, Is.EqualTo(maxHealth));
        Assert.That(server.Builds.SkillPointsLeft(player), Is.EqualTo(1), "a skill point to spend");
        (LogLevel level, EventId eventId, string message, IReadOnlyDictionary<string, object?> fields) logged =
            server.ProgressionLog.Entries.Single(entry => entry.EventId.Name == "JobLeveledUp");
        Assert.That((logged.level, logged.eventId.Id), Is.EqualTo((LogLevel.Information, 1011)));
        Assert.That((logged.fields["JobLevel"], logged.fields["PreviousJobLevel"]), Is.EqualTo((2, 1)));
        Assert.That(server.Store.Checkpoints.Count, Is.GreaterThan(checkpoints), "an important transition");
        Assert.That(server.Store.Checkpoints.Last().JobLevel, Is.EqualTo(2));
        Assert.That(Sheets(server, connection).Last().SkillPoints, Is.EqualTo(1));
    }

    [Test]
    public void Sheet_ForAStoredBuild_ShowsItsPointsItsCostsAndItsDerivedStatistics()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, 1);

        // Base level 10 grants 34 stat points; AGI 5 to 11 costs 2 × 6 = 12, so 22 are left; STR 5 costs 2 to raise and
        // AGI 11 costs 3. Job level 4 grants 3 skill points; Strike 1 leaves 2.
        server.Store.Edit(
            1,
            level: 10,
            jobLevel: 4,
            jobExperience: 7,
            stats: new PrimaryStats(5, 11, 5, 5, 5, 5),
            skills: new Dictionary<string, int> { ["skill.strike"] = 1 });
        server.SendEnterWorld(connection, 1);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        PlayerEntity player = server.PlayerOf(connection);

        CharacterSheet sheet = Sheets(server, connection).Single();

        Assert.That(
            (sheet.JobLevel, sheet.JobExperience, sheet.JobExperienceToNextLevel, sheet.StatPoints, sheet.SkillPoints),
            Is.EqualTo(((byte)4, 7ul, 120ul, (ushort)22, (byte)2)));
        Assert.That(
            sheet.Stats.Select(stat => (stat.Value, stat.NextCost)),
            Is.EqualTo(new[] { (5, 2), (11, 3), (5, 2), (5, 2), (5, 2), (5, 2) }
                .Select(pair => ((byte)pair.Item1, (byte)pair.Item2))));
        Assert.That(
            (sheet.Attack, sheet.MagicAttack, sheet.Defense, sheet.MagicDefense, sheet.Hit, sheet.Flee, sheet.Critical,
                sheet.AttackSpeed),
            Is.EqualTo(((ushort)player.Stats.PhysicalAttack, (ushort)player.Stats.MagicalAttack,
                (ushort)player.Stats.SoftDefense, (ushort)player.Stats.SoftMagicDefense, (ushort)player.Stats.Hit,
                (ushort)player.Stats.Flee, (ushort)player.Stats.Critical, (ushort)player.Stats.AttackSpeed)));
    }

    [Test]
    public void Sheet_WhenNothingOnItChanges_IsNotSentAgain()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId connection = server.EnterWorld(1);
        server.Transport.ClearSent();

        server.Tick(20);

        Assert.That(Sheets(server, connection), Is.Empty);
    }
}
}
