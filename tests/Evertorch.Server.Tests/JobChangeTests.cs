using System;
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
///     The job change at the Guildmaster (Gameplay Systems §6.1; Persistence §5): <see cref="ChangeJob" /> is checked in
///     order, committed at once with a weapon the new job cannot wield taken off, and answered only after the commit;
///     then a cast ends, the job is set at job level 1 with the learned skills kept, the owner is sent its skill list,
///     its sheet, and its health, those who see the character get its new body in a second spawn, and a checkpoint
///     follows. A lost answer is settled from the stored job.
/// </summary>
[TestFixture]
public sealed class JobChangeTests
{
    private const long Character = 1;
    private const long Observer = 2;
    private const string Guildmaster = "npc.guildmaster";
    private const string Quartermaster = "npc.quartermaster";
    private const string Vanguard = "job.vanguard";
    private const string Arcanist = "job.arcanist";
    private const string Staff = "item.weapon.training_staff";
    private const string Sword = "item.weapon.training_sword";

    // A character stored at Adventurer job level 10 with 5 of its 9 skill points left, holding what it is given
    // (worn when asked), standing beside the NPC, and an observer beside it; nothing sent since.
    private static (TestServer Server, ConnectionId Player, ConnectionId Observer) Beside(
        string npc,
        float distance = 2f,
        string? item = null,
        bool isWorn = false)
    {
        var server = new TestServer(withNpcs: true);
        server.Store.SeedOnCreate = BuildSeed.ReadyToChange;
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, Character);
        if (item != null)
        {
            server.Store.Edit(Character, item: item);
            if (isWorn)
            {
                server.Store.Wear(Character, server.Store.Stored(Character).Items.Single().Id, "Weapon");
            }
        }

        server.SendEnterWorld(player, Character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        ConnectionId observer = server.EnterWorld(Observer);
        NpcEntity at = server.NpcOf(npc);
        server.Place(player, at.Position.X + distance, at.Position.Z);
        server.Place(observer, at.Position.X + distance, at.Position.Z + 1f);
        server.Tick(2);
        server.Transport.ClearSent();
        return (server, player, observer);
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

    private static List<T> Sent<T>(TestServer server, ConnectionId player, MessageOpcode opcode, Func<byte[], T?> read)
        where T : class
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload))
            .Where(message => message != null)
            .Select(message => message!)
            .ToList();
    }

    private static void Change(TestServer server, ConnectionId player, string job, uint sequence = 1)
    {
        server.SendChangeJob(player, server.NpcOf(Guildmaster).Id, job, sequence);
        server.Tick();
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);
        server.Tick();
    }

    [TestCase(Quartermaster, 2f, Vanguard, CommandRejectionReason.InvalidTarget,
        TestName = "ChangeJob_AtAnNpcThatDoesNotOfferIt_IsRefused")]
    [TestCase(Guildmaster, 6f, Vanguard, CommandRejectionReason.OutOfRange,
        TestName = "ChangeJob_OutOfReach_IsRefused")]
    [TestCase(Guildmaster, 2f, "job.adventurer", CommandRejectionReason.RequirementNotMet,
        TestName = "ChangeJob_ToABaseJob_IsRefused")]
    [TestCase(Guildmaster, 2f, "job.missing", CommandRejectionReason.RequirementNotMet,
        TestName = "ChangeJob_ToNoSuchJob_IsRefused")]
    public void ChangeJob_ThatCannotBeMade_ChangesNothing(
        string npc,
        float distance,
        string job,
        CommandRejectionReason expected)
    {
        (TestServer server, ConnectionId player, _) = Beside(npc, distance);

        server.SendChangeJob(player, server.NpcOf(npc).Id, job, 7);
        server.Tick();

        Assert.That(
            Rejections(server, player).Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(new[] { (7u, expected) }));
        Assert.That(server.PlayerOf(player).Job.Value, Is.EqualTo("job.adventurer"));
        Assert.That(server.Store.JobChangeCommits, Is.Empty);
    }

    // The change is final: a first job is no first job of itself, nor of the other first job.
    [Test]
    public void ChangeJob_Again_IsRefused()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster);
        Change(server, player, Vanguard);
        server.PlayerOf(player).JobLevel = 10;
        server.Transport.ClearSent();

        server.SendChangeJob(player, server.NpcOf(Guildmaster).Id, Arcanist, 2);
        server.Tick();

        Assert.That(Rejections(server, player).Single().Reason, Is.EqualTo(CommandRejectionReason.RequirementNotMet));
        Assert.That(server.Store.JobChangeCommits.Count, Is.EqualTo(1));
    }

    [Test]
    public void ChangeJob_AtTheGuildmasterAtTheCap_CommitsItThenChangesTheJobAndTellsTheOwnerAndTheObserver()
    {
        (TestServer server, ConnectionId player, ConnectionId observer) = Beside(Guildmaster);
        PlayerEntity entity = server.PlayerOf(player);
        int checkpoints = server.Store.Checkpoints.Count;

        Change(server, player, Vanguard);

        JobChangeCommit commit = server.Store.JobChangeCommits.Single();
        Assert.That(
            (commit.CharacterId, commit.FromJob, commit.ToJob, commit.UnequipSlot),
            Is.EqualTo((Character, "job.adventurer", Vanguard, (string?)null)));
        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That((entity.Job.Value, entity.JobLevel, entity.JobExperience), Is.EqualTo((Vanguard, 1, 0L)));
        Assert.That(entity.Skills.Count, Is.EqualTo(3), "the learned skills stay");
        StoredCharacter stored = server.Store.Stored(Character);
        Assert.That((stored.JobDefinitionId, stored.JobLevel), Is.EqualTo((Vanguard, 1)), "committed");

        CharacterSheet sheet = Sent(server, player, MessageOpcode.CharacterSheet, payload =>
            CharacterSheet.TryRead(payload, out CharacterSheet? read) ? read : null).Last();
        Assert.That(
            (sheet.Job.Value, sheet.JobLevel, sheet.SkillPoints),
            Is.EqualTo((Vanguard, (ushort)1, (ushort)5)),
            "the 5 points left carried over (research note §4)");
        SkillList list = Sent(server, player, MessageOpcode.SkillList, payload =>
            SkillList.TryRead(payload, out SkillList? read) ? read : null).Last();
        Assert.That(list.Skills.Count, Is.EqualTo(6), "the whole tree");

        EntitySpawn respawn = Sent(server, observer, MessageOpcode.EntitySpawn, payload =>
                EntitySpawn.TryRead(payload, out EntitySpawn? read) ? read : null)
            .Single(spawn => spawn.Entity == entity.Id);
        Assert.That(respawn.DefinitionId, Is.EqualTo(Vanguard), "the new body");
        Assert.That(
            server.Transport.ControlOpcodesSentTo(observer),
            Has.None.EqualTo(MessageOpcode.EntityDespawn)
                .And.None.EqualTo(MessageOpcode.CharacterSheet)
                .And.None.EqualTo(MessageOpcode.SkillList),
            "no despawn, and nothing of the owner's build");
        Assert.That(
            server.Transport.ControlOpcodesSentTo(player).Where(opcode => opcode == MessageOpcode.EntitySpawn),
            Is.Empty,
            "the owner learns its job from the sheet");
        Assert.That(entity.RespawnPending, Is.False, "sent once");
        Assert.That(server.Store.Checkpoints.Count, Is.GreaterThan(checkpoints), "a checkpoint follows");
        Assert.That(server.Store.Checkpoints.Last().Job, Is.EqualTo(Vanguard));
    }

    [Test]
    public void ChangeJob_BelowTheBaseJobsCap_IsRefused()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster);
        server.PlayerOf(player).JobLevel = 9;

        server.SendChangeJob(player, server.NpcOf(Guildmaster).Id, Vanguard, 7);
        server.Tick();

        Assert.That(Rejections(server, player).Single().Reason, Is.EqualTo(CommandRejectionReason.RequirementNotMet));
        Assert.That(server.Store.JobChangeCommits, Is.Empty);
    }

    [Test]
    public void ChangeJob_DuringACast_EndsTheCast()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster);
        PlayerEntity entity = server.PlayerOf(player);
        entity.SetSkillLevel(new SkillDefinitionId("skill.first_aid"), 1);
        server.SendUseSkill(player, "skill.first_aid", default, 1);
        server.Tick();
        Assert.That(entity.Combat.IsCasting, Is.True, "casting");

        Change(server, player, Vanguard, 2);

        Assert.That(entity.Combat.IsCasting, Is.False);
    }

    [Test]
    public void ChangeJob_IsLoggedWithTheItemTakenOff()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster, item: Staff, isWorn: true);

        Change(server, player, Vanguard);

        (LogLevel level, EventId eventId, string _, IReadOnlyDictionary<string, object?> fields) changed =
            server.BuildsLog.Entries.Single(entry => entry.EventId.Id == 1015);
        Assert.That((changed.level, changed.eventId.Name), Is.EqualTo((LogLevel.Information, "JobChanged")));
        Assert.That(
            (changed.fields["Character"], changed.fields["PreviousJob"], changed.fields["Job"], changed.fields["Item"]),
            Is.EqualTo(
                ((object?)Character, (object?)new JobDefinitionId("job.adventurer"),
                    (object?)new JobDefinitionId(Vanguard), (object?)Staff)));
    }

    [Test]
    public void ChangeJob_ThatNeverCommitted_IsRefusedAsServiceUnavailableOnceTheStoreAnswers()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster);
        server.SendChangeJob(player, server.NpcOf(Guildmaster).Id, Vanguard, 1);
        server.Store.IsUnavailable = true;
        server.Tick(3);

        server.Store.IsUnavailable = false;
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);

        Assert.That(
            Rejections(server, player).Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(new[] { (1u, CommandRejectionReason.ServiceUnavailable) }));
        Assert.That(server.PlayerOf(player).Job.Value, Is.EqualTo("job.adventurer"));
        Assert.That(server.Store.Stored(Character).JobDefinitionId, Is.EqualTo("job.adventurer"));
    }

    [Test]
    public void ChangeJob_WhileAnotherOperationIsInFlight_IsRefusedWithItemActionInFlight()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster, item: Sword);
        long sword = server.Store.Stored(Character).Items.Single().Id;
        server.Store.IsUnavailable = true;
        server.SendEquip(player, sword, 1);
        server.Tick();

        server.SendChangeJob(player, server.NpcOf(Guildmaster).Id, Vanguard, 2);
        server.Tick();

        Assert.That(
            Rejections(server, player).Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(new[] { (2u, CommandRejectionReason.ItemActionInFlight) }));
        server.Store.IsUnavailable = false;
    }

    [Test]
    public void ChangeJob_WhileDeadOrLoggingOut_IsRefusedAsNotAllowedNow()
    {
        (TestServer dead, ConnectionId deadPlayer, _) = Beside(Guildmaster);
        dead.Combat.Kill(dead.World.Maps.Single(), dead.PlayerOf(deadPlayer), null, dead.CurrentTick);
        (TestServer leaving, ConnectionId leavingPlayer, _) = Beside(Guildmaster);
        leaving.SessionOf(leavingPlayer).Character!.IsLoggingOut = true;

        dead.SendChangeJob(deadPlayer, dead.NpcOf(Guildmaster).Id, Vanguard, 1);
        dead.Tick();
        leaving.SendChangeJob(leavingPlayer, leaving.NpcOf(Guildmaster).Id, Vanguard, 1);
        leaving.Tick();

        Assert.That(Rejections(dead, deadPlayer).Single().Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(
            Rejections(leaving, leavingPlayer).Single().Reason,
            Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(dead.Store.JobChangeCommits.Concat(leaving.Store.JobChangeCommits), Is.Empty);
    }

    // A commit whose answer was lost is settled from the stored job, and the change applies once (Persistence §5).
    [Test]
    public void ChangeJob_WhoseAnswerIsLost_IsSettledFromTheStoredJobOnce()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster, item: Staff, isWorn: true);
        server.Store.AmbiguousJobChangeFailures = 100;

        server.SendChangeJob(player, server.NpcOf(Guildmaster).Id, Vanguard, 1);
        server.Tick(2);

        Assert.That(server.PlayerOf(player).Job.Value, Is.EqualTo("job.adventurer"), "no answer yet");
        Assert.That(server.Store.Stored(Character).JobDefinitionId, Is.EqualTo(Vanguard), "yet committed");
        Assert.That(
            server.ItemActionLog.Entries.Any(entry => entry.EventId.Name == "InventoryOperationUnsettled"),
            Is.True);

        server.Store.AmbiguousJobChangeFailures = 0;
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);
        server.Tick();

        Assert.That((server.PlayerOf(player).Job.Value, server.PlayerOf(player).JobLevel), Is.EqualTo((Vanguard, 1)));
        Assert.That(server.PlayerOf(player).Weapon, Is.Null, "the staff came off with it");
        Assert.That(server.BuildsLog.Entries.Count(entry => entry.EventId.Id == 1015), Is.EqualTo(1));
        Assert.That(Rejections(server, player), Is.Empty);
    }

    // A worn weapon the new job cannot wield comes off in the change's own commit (owner decision 2).
    [Test]
    public void ChangeJob_WithAStaffWorn_TakesItOffInTheSameCommit()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster, item: Staff, isWorn: true);
        PlayerEntity entity = server.PlayerOf(player);
        long staff = server.Store.Stored(Character).Items.Single().Id;
        Assert.That(entity.Weapon, Is.Not.Null, "worn before");

        Change(server, player, Vanguard);

        Assert.That(server.Store.JobChangeCommits.Single().UnequipSlot, Is.EqualTo("Weapon"));
        Assert.That(server.Store.Stored(Character).Items.Single().EquippedSlot, Is.Null, "taken off, still held");
        Assert.That(entity.Weapon, Is.Null);
        InventoryChanged change = Sent(server, player, MessageOpcode.InventoryChanged, payload =>
            InventoryChanged.TryRead(payload, out InventoryChanged? read) ? read : null).Single();
        Assert.That(
            change.Changes.Select(row => (row.InventoryItem, row.Slot)),
            Is.EqualTo(new[] { (staff, EquipmentSlot.None) }));
    }

    [Test]
    public void ChangeJob_WithAWeaponTheNewJobWields_LeavesItWorn()
    {
        (TestServer server, ConnectionId player, _) = Beside(Guildmaster, item: Sword, isWorn: true);

        Change(server, player, Vanguard);

        Assert.That(server.Store.JobChangeCommits.Single().UnequipSlot, Is.Null);
        Assert.That(server.Store.Stored(Character).Items.Single().EquippedSlot, Is.EqualTo("Weapon"));
        Assert.That(server.PlayerOf(player).Weapon, Is.Not.Null);
        Assert.That(server.Transport.ControlOpcodesSentTo(player), Has.None.EqualTo(MessageOpcode.InventoryChanged));
    }
}
}
