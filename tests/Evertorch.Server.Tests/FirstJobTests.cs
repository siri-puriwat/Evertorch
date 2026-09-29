using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A first job on the server, before the wire can change one (Gameplay Systems §2.1, §9, §11.1; Persistence §6):
///     it learns from its base job's tree and its own, carries the base job's points, lists its whole tree, is reset
///     across both trees keeping its job, wields only its own weapon type, and keeps a weapon worn at load that it
///     cannot wield, which the load logs.
/// </summary>
[TestFixture]
public sealed class FirstJobTests
{
    private const long Character = 1;
    private const string Vanguard = "job.vanguard";
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";
    private const string Focus = "skill.focus";
    private const string HeavyBlow = "skill.heavy_blow";
    private const string WarCry = "skill.war_cry";
    private const string IronGuard = "skill.iron_guard";
    private const string Mend = "skill.mend";
    private const string Sword = "item.weapon.training_sword";
    private const string Staff = "item.weapon.training_staff";

    // A Vanguard at jobLevel with the learned skills, entered and ticked once with nothing sent since.
    private static (TestServer Server, ConnectionId Player) AsVanguard(int jobLevel,
        params (string Skill, int Level)[] learned)
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.EnterWorld(Character);
        PlayerEntity entity = server.PlayerOf(player);
        entity.ChangeJob(new JobDefinitionId(Vanguard));
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

    private static (uint Sequence, CommandRejectionReason Reason)[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return (rejected.CommandSequence, rejected.Reason);
            })
            .ToArray();
    }

    private static SkillList LastSkillList(TestServer server, ConnectionId player)
    {
        return Read(server, player, MessageOpcode.SkillList, (byte[] payload, out SkillList? read) =>
            SkillList.TryRead(payload, out read)).Last();
    }

    private delegate bool TryReader<T>(byte[] payload, out T? message);

    // The research note's vectors (research/jobs-and-job-change.md §4): the Adventurer's job cap 10 carries 9 points,
    // so a first job with the Adventurer's whole tree learned has 0 left at job level 1 and 3 at job level 4.
    [TestCase(1, 0)]
    [TestCase(4, 3)]
    [TestCase(10, 9)]
    public void SkillPoints_OfAFirstJob_CarryItsBaseJobsNine(int jobLevel, int left)
    {
        (TestServer server, ConnectionId player) = AsVanguard(jobLevel, (Strike, 5), (FirstAid, 1), (Focus, 3));

        Assert.That(server.Builds.SkillPointsLeft(server.PlayerOf(player)), Is.EqualTo(left));
    }

    // Another first job's skill is outside the tree, and War Cry's prerequisite is Heavy Blow 1 (Gameplay Systems §9).
    [TestCase(Mend, TestName = "LearnSkill_OfAnotherFirstJobsSkill_IsRefused")]
    [TestCase(WarCry, TestName = "LearnSkill_OfAFirstJobsSkillWithoutItsPrerequisite_IsRefused")]
    public void LearnSkill_ThatAFirstJobCannotLearn_IsRefusedWithRequirementNotMet(string skill)
    {
        (TestServer server, ConnectionId player) = AsVanguard(1);

        server.SendLearnSkill(player, skill, 4);
        server.Tick();

        Assert.That(Rejections(server, player), Is.EqualTo(new[] { (4u, CommandRejectionReason.RequirementNotMet) }));
        Assert.That(server.PlayerOf(player).Skills, Is.Empty);
    }

    // A first job's build is overspent only past its whole pool: 18 at job level 10 (Gameplay Systems §2.1).
    [TestCase(9, false)]
    [TestCase(10, true)]
    public void IsOverspent_ForAFirstJob_CountsTheCarriedPointsAndTheWholeTree(int heavyBlowAndWarCry, bool isOverspent)
    {
        int warCry = Math.Min(3, heavyBlowAndWarCry - 5);
        (TestServer server, ConnectionId player) = AsVanguard(
            10,
            (Strike, 5),
            (FirstAid, 1),
            (Focus, 3),
            (HeavyBlow, 5),
            (WarCry, warCry),
            (IronGuard, heavyBlowAndWarCry - 5 - warCry));

        Assert.That(server.Builds.IsOverspent(server.PlayerOf(player)), Is.EqualTo(isOverspent));
    }

    // A first job wields only its own weapon type; the Adventurer both (Gameplay Systems §11.1).
    [TestCase(Staff, true, TestName = "Equip_OfAWeaponItsJobCannotWield_IsRefusedWithRequirementNotMet")]
    [TestCase(Sword, false, TestName = "Equip_OfItsOwnWeaponType_IsCommitted")]
    public void Equip_AsAVanguard_TakesOnlyASword(string item, bool isRefused)
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, Character);
        server.Store.Edit(Character, Vanguard, item: item);
        server.SendEnterWorld(player, Character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        long row = server.Store.Stored(Character).Items.Single().Id;
        server.Transport.ClearSent();

        server.SendEquip(player, row, 5);
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            isRefused ? Is.EqualTo(new[] { (5u, CommandRejectionReason.RequirementNotMet) }) : Is.Empty);
        Assert.That(server.Store.Stored(Character).Items.Single().EquippedSlot,
            isRefused ? Is.Null : Is.EqualTo("Weapon"));
        Assert.That(server.PlayerOf(player).Weapon, isRefused ? Is.Null : Is.Not.Null);
    }

    // Content that lost the Vanguard's base job takes away the carried points and the base tree, so a build that
    // spent them is overspent and the load resets it (Persistence §6).
    [Test]
    public void IsOverspent_ForAFirstJobWhoseContentLostItsBaseJob_IsTrue()
    {
        (TestServer server, ConnectionId player) = AsVanguard(4, (Strike, 1));
        JobDefinition vanguard = server.Content.Jobs[new JobDefinitionId(Vanguard)];
        var standalone = new JobDefinition(
            vanguard.Id,
            vanguard.DisplayName,
            vanguard.StartingStats,
            vanguard.HealthBase,
            vanguard.HealthPerLevel,
            vanguard.SpiritBase,
            vanguard.SpiritPerLevel,
            vanguard.UnarmedAttackSpeedPenalty,
            vanguard.BaseSpeed,
            vanguard.StartingMap,
            vanguard.BasicAttack,
            vanguard.ExperienceTable,
            vanguard.JobExperienceTable,
            vanguard.Skills,
            vanguard.Weapons);
        var jobs = new Dictionary<JobDefinitionId, JobDefinition>(server.Content.Jobs) { [vanguard.Id] = standalone };
        ServerContent content = server.Content;
        var lost = new ServerContent(
            content.ServerContentVersion,
            content.ClientContentVersion,
            new Dictionary<ItemDefinitionId, ItemDefinition>(content.Items),
            new Dictionary<MonsterDefinitionId, MonsterDefinition>(content.Monsters),
            new Dictionary<SkillDefinitionId, SkillDefinition>(content.Skills),
            jobs,
            new Dictionary<MapDefinitionId, MapDefinition>(content.Maps),
            new Dictionary<ExperienceDefinitionId, ExperienceTableDefinition>(content.ExperienceTables),
            new Dictionary<StatusDefinitionId, StatusEffectDefinition>(content.StatusEffects),
            new Dictionary<NpcDefinitionId, NpcDefinition>(content.Npcs),
            new Dictionary<QuestDefinitionId, QuestDefinition>(content.Quests));
        var builds = new CharacterBuilds(
            lost,
            new RenewalProgressionRules(),
            server.Stats,
            new MessageSender(server.Transport),
            server.Instruments,
            new CapturingLogger<CharacterBuilds>());

        Assert.That(server.Builds.IsOverspent(server.PlayerOf(player)), Is.False, "with its base job");
        Assert.That(builds.IsOverspent(server.PlayerOf(player)), Is.True, "without it");
    }

    [Test]
    public void LearnSkill_AsAFirstJob_LearnsFromItsBaseTreeAndItsOwn()
    {
        (TestServer server, ConnectionId player) = AsVanguard(1);

        server.SendLearnSkill(player, Strike, 1);
        server.SendLearnSkill(player, HeavyBlow, 2);
        server.SendLearnSkill(player, WarCry, 3);
        server.Tick();

        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That(
            server.PlayerOf(player).Skills.ToDictionary(pair => pair.Key.Value, pair => pair.Value),
            Is.EquivalentTo(new Dictionary<string, int> { [Strike] = 1, [HeavyBlow] = 1, [WarCry] = 1 }));
        Assert.That(server.Builds.SkillPointsLeft(server.PlayerOf(player)), Is.EqualTo(6), "9 carried, 3 spent");
    }

    // Only changed content or a hand-edited row leaves a worn weapon the job cannot wield: it stays worn and counts, and
    // the load logs it (Gameplay Systems §11.1).
    [Test]
    public void Load_OfAFirstJobWearingAWeaponItCannotWield_KeepsItWornAndLogsIt()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, Character);
        server.Store.Edit(Character, Vanguard, item: Staff);
        long staff = server.Store.Stored(Character).Items.Single().Id;
        server.Store.Wear(Character, staff, "Weapon");

        server.SendEnterWorld(player, Character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);

        Assert.That(server.PlayerOf(player).Weapon, Is.Not.Null, "worn and counted");
        (LogLevel level, EventId eventId, string _, IReadOnlyDictionary<string, object?> fields) logged =
            server.LifetimeLog.Entries.Single(entry => entry.EventId.Id == 1016);
        Assert.That((logged.level, logged.eventId.Name), Is.EqualTo((LogLevel.Warning, "WeaponNotWieldable")));
        Assert.That(
            (logged.fields["Character"], logged.fields["Job"], logged.fields["Item"]),
            Is.EqualTo(((object?)Character, (object?)new JobDefinitionId(Vanguard),
                (object?)new ItemDefinitionId(Staff))));
    }

    [Test]
    public void Load_OfAnAdventurerWearingEitherWeapon_LogsNothing()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, Character);
        server.Store.Edit(Character, item: Staff);
        server.Store.Wear(Character, server.Store.Stored(Character).Items.Single().Id, "Weapon");

        server.SendEnterWorld(player, Character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);

        Assert.That(server.LifetimeLog.Entries.Where(entry => entry.EventId.Id == 1016), Is.Empty);
    }

    // Until line 6 lets a player be selected, an ally skill lands on its caster alone, never on a monster.
    [Test]
    public void Mend_BeforePlayersCanBeSelected_HealsTheCaster()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.EnterWorld(Character);
        PlayerEntity entity = server.PlayerOf(player);
        entity.ChangeJob(new JobDefinitionId("job.arcanist"));
        entity.SetSkillLevel(new SkillDefinitionId(Mend), 1);
        entity.CurrentHealth = 10;
        entity.CurrentSpirit = entity.MaxSpirit;

        server.SendUseSkill(player, Mend, default, 1);
        server.Tick(30);

        SkillResolved healed = Read(server, player, MessageOpcode.SkillResolved,
            (byte[] payload, out SkillResolved? read) =>
                SkillResolved.TryRead(payload, out read)).Single();
        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That((healed.Caster, healed.Target, healed.Amount), Is.EqualTo((entity.Id, entity.Id, 40u)), "Mend 1");
        Assert.That(entity.CurrentHealth, Is.GreaterThanOrEqualTo(50));
    }

    // The Guildmaster's reset returns the points of both trees and keeps the job (Gameplay Systems §6.1).
    [Test]
    public void Reset_OfAFirstJob_ForgetsBothTreesAndKeepsTheJob()
    {
        (TestServer server, ConnectionId player) = AsVanguard(3, (Strike, 2), (HeavyBlow, 1));
        PlayerEntity entity = server.PlayerOf(player);
        entity.Level = 3;
        entity.SetPrimary(new PrimaryStats(9, 5, 5, 5, 5, 5));

        server.Builds.ResetAtGuildmaster(entity, player);

        Assert.That(entity.Skills, Is.Empty);
        Assert.That(entity.Job.Value, Is.EqualTo(Vanguard));
        Assert.That(entity.Primary, Is.EqualTo(new PrimaryStats(5, 5, 5, 5, 5, 5)), "the Adventurer's start");
        Assert.That(server.Builds.SkillPointsLeft(entity), Is.EqualTo(11), "9 carried and 2 of job level 3");
    }

    [Test]
    public void SkillList_OfAFirstJob_NamesItsWholeTreeInOrder()
    {
        (TestServer server, ConnectionId player) = AsVanguard(2, (Strike, 1));

        server.SendLearnSkill(player, HeavyBlow, 1);
        server.Tick();

        SkillList list = LastSkillList(server, player);
        Assert.That(
            list.Skills.Select(entry => (entry.Skill.Value, entry.Level)),
            Is.EqualTo(
                new[]
                {
                    (Strike, (byte)1), (FirstAid, (byte)0), (Focus, (byte)0), (HeavyBlow, (byte)1),
                    (WarCry, (byte)0), (IronGuard, (byte)0)
                }));
        Assert.That(
            list.Skills.Select(entry => (entry.PrerequisiteIndex, entry.PrerequisiteLevel)),
            Is.EqualTo(
                new[]
                {
                    (SkillListEntry.NoPrerequisite, (byte)0), (SkillListEntry.NoPrerequisite, (byte)0),
                    ((byte)0, (byte)1), (SkillListEntry.NoPrerequisite, (byte)0), ((byte)3, (byte)1),
                    ((byte)4, (byte)1)
                }),
            "a prerequisite points into the whole tree");
    }
}
}
