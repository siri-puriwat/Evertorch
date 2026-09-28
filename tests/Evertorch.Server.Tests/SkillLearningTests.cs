using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Spending skill points (Gameplay Systems §9): <see cref="LearnSkill" /> learns one level of a skill of the job's
///     tree, answered with the skill list and the sheet; a skill outside the tree, at its maximum, or without its
///     prerequisite is refused before a missing point.
/// </summary>
[TestFixture]
public sealed class SkillLearningTests
{
    private const long Character = 1;
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";
    private const string Focus = "skill.focus";

    private static (TestServer Server, ConnectionId Player) AtJobLevel(int jobLevel,
        params (string Skill, int Level)[] learned)
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.EnterWorld(Character);
        PlayerEntity entity = server.PlayerOf(player);
        entity.JobLevel = jobLevel;
        foreach ((string skill, int level) in learned)
        {
            entity.SetSkillLevel(new SkillDefinitionId(skill), level);
        }

        server.Tick();
        server.Transport.ClearSent();
        return (server, player);
    }

    private static List<T> Read<T>(TestServer server, ConnectionId player, MessageOpcode opcode, TryReader<T> read)
        where T : class
    {
        var messages = new List<T>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == opcode && read(message.Payload, out T? found))
            {
                messages.Add(found!);
            }
        }

        return messages;
    }

    private static CommandRejected[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    private delegate bool TryReader<T>(byte[] payload, out T? message);

    // Job level N grants N - 1 points; Focus requires Strike 1, and First Aid stops at 1 (Gameplay Systems §9).
    [TestCase(2, null, "skill.spark_bolt", CommandRejectionReason.RequirementNotMet,
        TestName = "LearnSkill_OutsideTheJobsTree_IsRefused")]
    [TestCase(2, null, "skill.nothing", CommandRejectionReason.RequirementNotMet,
        TestName = "LearnSkill_OfNoSuchSkill_IsRefused")]
    [TestCase(3, FirstAid, FirstAid, CommandRejectionReason.RequirementNotMet,
        TestName = "LearnSkill_AtItsMaximum_IsRefused")]
    [TestCase(2, null, Focus, CommandRejectionReason.RequirementNotMet,
        TestName = "LearnSkill_WithoutItsPrerequisite_IsRefused")]
    [TestCase(1, null, Focus, CommandRejectionReason.RequirementNotMet,
        TestName = "LearnSkill_WithoutItsPrerequisite_IsRefusedBeforeThePoints")]
    [TestCase(2, FirstAid, Strike, CommandRejectionReason.NotEnoughPoints,
        TestName = "LearnSkill_WithNoPointLeft_IsRefused")]
    public void LearnSkill_ThatCannotBeLearned_ChangesNothing(
        int jobLevel,
        string? learned,
        string skill,
        CommandRejectionReason expected)
    {
        (TestServer server, ConnectionId player) = learned == null
            ? AtJobLevel(jobLevel)
            : AtJobLevel(jobLevel, (learned, 1));
        var before = server.PlayerOf(player).Skills.ToDictionary(pair => pair.Key, pair => pair.Value);

        server.SendLearnSkill(player, skill, 7);
        server.Tick();

        Assert.That(
            Rejections(server, player).Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(new[] { (7u, expected) }));
        Assert.That(server.PlayerOf(player).Skills, Is.EquivalentTo(before));
        Assert.That(server.Transport.ControlSentTo(player).Any(message => message.Opcode == MessageOpcode.SkillList),
            Is.False);
        Assert.That(server.BuildsLog.Entries, Is.Empty);
    }

    [Test]
    public void LearnSkill_IsKeptByTheNextCheckpoint_AndCanThenBeUsed()
    {
        (TestServer server, ConnectionId player) = AtJobLevel(2);
        server.SendLearnSkill(player, FirstAid, 1);
        server.Tick();
        int before = server.Store.Checkpoints.Count;

        server.Lifetime.QueueCheckpoint(server.SessionOf(player).Character!);
        server.TickUntil(() => server.Store.Checkpoints.Count > before);
        server.Transport.ClearSent();
        server.SendUseSkill(player, FirstAid, default, 2);
        server.Tick();

        CharacterCheckpoint checkpoint = server.Store.Checkpoints.Last();
        Assert.That(checkpoint.Skills!.Select(skill => (skill.SkillDefinitionId, skill.Level)),
            Is.EqualTo(new[] { (FirstAid, 1) }));
        Assert.That(server.Store.Stored(Character).Skills.Single().SkillDefinitionId, Is.EqualTo(FirstAid));
        Assert.That(Rejections(server, player), Is.Empty, "a learned skill can be used");
        Assert.That(server.PlayerOf(player).Combat.IsCasting, Is.True);
    }

    [Test]
    public void LearnSkill_WhileDeadOrLoggingOut_IsRefusedAsNotAllowedNow()
    {
        (TestServer dead, ConnectionId deadPlayer) = AtJobLevel(2);
        dead.Combat.Kill(dead.World.Maps.Single(), dead.PlayerOf(deadPlayer), null, dead.CurrentTick);
        (TestServer leaving, ConnectionId leavingPlayer) = AtJobLevel(2);
        leaving.SessionOf(leavingPlayer).Character!.IsLoggingOut = true;

        dead.SendLearnSkill(deadPlayer, Strike, 1);
        dead.Tick();
        leaving.SendLearnSkill(leavingPlayer, Strike, 1);
        leaving.Tick();

        Assert.That(Rejections(dead, deadPlayer).Single().Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(Rejections(leaving, leavingPlayer).Single().Reason,
            Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(dead.PlayerOf(deadPlayer).Skills, Is.Empty);
        Assert.That(leaving.PlayerOf(leavingPlayer).Skills, Is.Empty);
    }

    [Test]
    public void LearnSkill_WithAPoint_LearnsOneLevel_AndTheListAndTheSheetAnswerInTheTick()
    {
        (TestServer server, ConnectionId player) = AtJobLevel(3, (Strike, 1));

        server.SendLearnSkill(player, Strike, 1);
        server.Tick();

        SkillList list = Read(server, player, MessageOpcode.SkillList, (byte[] payload, out SkillList? read) =>
            SkillList.TryRead(payload, out read)).Single();
        CharacterSheet sheet = Read(
            server,
            player,
            MessageOpcode.CharacterSheet,
            (byte[] payload, out CharacterSheet? read) => CharacterSheet.TryRead(payload, out read)).Single();
        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That(server.PlayerOf(player).Skills[new SkillDefinitionId(Strike)], Is.EqualTo(2));
        Assert.That(list.Skills[0].Level, Is.EqualTo((byte)2));
        Assert.That(list.Skills[0].SpCost, Is.EqualTo(9u), "Strike 2's values");
        Assert.That(sheet.SkillPoints, Is.Zero, "job level 3 grants 2, both spent");
        (LogLevel level, EventId eventId, string _, IReadOnlyDictionary<string, object?> fields) learned =
            server.BuildsLog.Entries.Single();
        Assert.That((learned.level, learned.eventId.Id, learned.eventId.Name),
            Is.EqualTo((LogLevel.Information, 1013, "SkillLearned")));
        Assert.That(
            (learned.fields["Character"], learned.fields["Skill"], learned.fields["Level"]),
            Is.EqualTo(((object?)Character, (object?)new SkillDefinitionId(Strike), (object?)2)));
    }
}
}
