using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A character's build as it is loaded and checkpointed (Persistence §6, §7; Gameplay Systems §2, §9): the job level
///     and job experience, the primary statistics, and the learned skills, and the reset of a stored build that spends
///     more than its levels grant.
/// </summary>
[TestFixture]
public sealed class CharacterBuildLoadTests
{
    private const long Character = 1;
    private static readonly PrimaryStats Start = new(5, 5, 5, 5, 5, 5);

    private static (TestServer Server, ConnectionId Player) SignedIn(bool withAdventurerBuild = true)
    {
        var server = new TestServer(withAdventurerBuild: withAdventurerBuild);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, Character);
        return (server, player);
    }

    private static void Enter(TestServer server, ConnectionId player)
    {
        server.SendEnterWorld(player, Character);
        server.TickUntil(() => server.SessionOf(player).State != SessionState.EnteringWorld);
        Assert.That(server.SessionOf(player).State, Is.EqualTo(SessionState.InWorld), "entered");
    }

    private static CharacterCheckpoint CheckpointNow(TestServer server, ConnectionId player)
    {
        int before = server.Store.Checkpoints.Count;
        server.Lifetime.QueueCheckpoint(server.SessionOf(player).Character!);
        server.TickUntil(() => server.Store.Checkpoints.Count > before);
        return server.Store.Checkpoints.Last();
    }

    private static Dictionary<string, int> LevelsOf(IEnumerable<StoredSkill> skills)
    {
        return skills.ToDictionary(skill => skill.SkillDefinitionId, skill => skill.Level);
    }

    private static Dictionary<string, int> LevelsOf(PlayerEntity player)
    {
        return player.Skills.ToDictionary(skill => skill.Key.Value, skill => skill.Value);
    }

    // A stored build that spends more than its levels grant, holds a statistic below the job's start, or has learned a
    // skill outside the job's tree starts again from the job's start; the load logs it, and the next checkpoint
    // stores it (Persistence §6).
    [TestCase(1, 1, 6, "skill.strike", 1, TestName = "Load_WithAStatRaisedAtLevelOne_ResetsTheBuild")]
    [TestCase(5, 1, 4, "skill.strike", 1, TestName = "Load_WithAStatBelowTheJobsStart_ResetsTheBuild")]
    [TestCase(5, 2, 5, "skill.strike", 2, TestName = "Load_WithMoreSkillLevelsThanTheJobLevelsGrant_ResetsTheBuild")]
    [TestCase(5, 6, 5, "skill.spark_bolt", 1, TestName = "Load_WithASkillOutsideTheJobsTree_ResetsTheBuild")]
    [TestCase(5, 6, 5, "skill.focus", 4, TestName = "Load_WithASkillAboveItsMaximum_ResetsTheBuild")]
    [TestCase(5, 2, 5, "skill.focus", 1, TestName = "Load_WithASkillWhosePrerequisiteIsUnmet_ResetsTheBuild")]
    public void Load_WithAnOverspentBuild_ResetsItLogsItAndTheNextCheckpointStoresIt(
        int level,
        int jobLevel,
        int agility,
        string skill,
        int skillLevel)
    {
        (TestServer server, ConnectionId player) = SignedIn();
        server.Store.Edit(
            Character,
            level: level,
            jobLevel: jobLevel,
            stats: new PrimaryStats(5, agility, 5, 5, 5, 5),
            skills: new Dictionary<string, int> { [skill] = skillLevel });

        Enter(server, player);
        PlayerEntity entered = server.PlayerOf(player);
        CharacterCheckpoint checkpoint = CheckpointNow(server, player);

        Assert.That(entered.Primary, Is.EqualTo(Start), "back to the job's start");
        Assert.That(entered.Skills, Is.Empty, "every skill forgotten");
        DerivedStats expected = new CharacterStats(new RenewalCharacterRules())
            .Calculate(server.Content.Jobs[entered.Job], level, Start);
        Assert.That(
            (entered.Stats.Flee, entered.Stats.AttackSpeed, entered.MaxHealth),
            Is.EqualTo((expected.Flee, expected.AttackSpeed, expected.MaxHp)),
            "derived again");
        Assert.That(entered.JobLevel, Is.EqualTo(jobLevel), "the job level stays");
        (LogLevel level, EventId eventId, string _, IReadOnlyDictionary<string, object?> fields) reset =
            server.BuildsLog.Entries.Single();
        Assert.That(reset.level, Is.EqualTo(LogLevel.Information));
        Assert.That((reset.eventId.Id, reset.eventId.Name), Is.EqualTo((1014, "BuildReset")));
        Assert.That(reset.fields["Reason"], Is.EqualTo(CharacterBuilds.OverspentReason));
        Assert.That(reset.fields["Character"], Is.EqualTo(Character));
        Assert.That(checkpoint.Stats, Is.EqualTo(Start));
        Assert.That(checkpoint.Skills, Is.Empty);
        Assert.That(server.Store.Stored(Character).Stats, Is.EqualTo(Start), "stored");
        Assert.That(server.Store.Stored(Character).Skills, Is.Empty, "stored");
    }

    [Test]
    public void Enter_WithAStoredBuild_LoadsTheJobPairTheStatisticsAndTheSkills_AndTheCheckpointCarriesThem()
    {
        (TestServer server, ConnectionId player) = SignedIn();

        // Base level 5 grants 13 stat points; AGI 5 to 9 costs 8. Job level 4 grants 3 skill points; Strike 2 costs 2.
        var raised = new PrimaryStats(5, 9, 5, 5, 5, 5);
        server.Store.Edit(
            Character,
            level: 5,
            jobLevel: 4,
            jobExperience: 12,
            stats: raised,
            skills: new Dictionary<string, int> { ["skill.strike"] = 2 });
        Enter(server, player);
        PlayerEntity entered = server.PlayerOf(player);
        CharacterCheckpoint checkpoint = CheckpointNow(server, player);

        Assert.That((entered.JobLevel, entered.JobExperience), Is.EqualTo((4, 12L)));
        Assert.That(entered.Primary, Is.EqualTo(raised));
        Assert.That(LevelsOf(entered), Is.EqualTo(new Dictionary<string, int> { ["skill.strike"] = 2 }));
        Assert.That(server.Builds.StatPointsLeft(entered), Is.EqualTo(5));
        Assert.That(server.Builds.SkillPointsLeft(entered), Is.EqualTo(1));
        Assert.That(server.BuildsLog.Entries, Is.Empty, "nothing reset");
        Assert.That((checkpoint.JobLevel, checkpoint.JobExperience), Is.EqualTo((4, 12L)));
        Assert.That(checkpoint.Stats, Is.EqualTo(raised));
        Assert.That(LevelsOf(checkpoint.Skills!), Is.EqualTo(new Dictionary<string, int> { ["skill.strike"] = 2 }));
    }

    [Test]
    public void Enter_WithAStoredSkillTheContentLacks_IsRefused_AndTheRowIsKept()
    {
        (TestServer server, ConnectionId player) = SignedIn();
        server.Store.Edit(Character, skills: new Dictionary<string, int> { ["skill.retired"] = 1 });

        server.SendEnterWorld(player, Character);
        server.TickUntil(() => server.SessionOf(player).State != SessionState.EnteringWorld);

        Assert.That(server.SessionOf(player).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(
            server.Log.Entries
                .Where(entry => entry.EventId.Name == "CharacterContentMismatch")
                .Select(entry => entry.Message),
            Has.Some.Contains("skill 'skill.retired'"));
        Assert.That(server.Store.Stored(Character).Skills.Single().SkillDefinitionId, Is.EqualTo("skill.retired"));
    }

    [Test]
    public void TestServer_ByDefault_SeedsTheAdventurerBuild_AndWithoutItStoresANewCharacterAsMade()
    {
        (TestServer seeded, ConnectionId _) = SignedIn();
        (TestServer plain, ConnectionId _) = SignedIn(false);

        StoredCharacter withBuild = seeded.Store.Stored(Character);
        StoredCharacter asMade = plain.Store.Stored(Character);

        Assert.That(withBuild.JobLevel, Is.EqualTo(BuildSeed.Adventurer.JobLevel));
        Assert.That(LevelsOf(withBuild.Skills), Is.EqualTo(BuildSeed.Adventurer.Skills));
        Assert.That((asMade.JobLevel, asMade.JobExperience), Is.EqualTo((1, 0L)));
        Assert.That(asMade.Skills, Is.Empty);
    }
}
}
