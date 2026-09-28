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
///     The Guildmaster's reset (Gameplay Systems §6.1): <see cref="ResetBuild" /> at an NPC that offers it returns every
///     stat and skill point for free, interrupts a cast, leaves status effects to run out, and queues a checkpoint at
///     once; the owner is sent the skill list, the sheet, and new maximums.
/// </summary>
[TestFixture]
public sealed class ResetBuildTests
{
    private const long Character = 1;
    private const string Guildmaster = "npc.guildmaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Health = "item.consumable.minor_health";
    private static readonly PrimaryStats Start = new(5, 5, 5, 5, 5, 5);

    // A character at base level 10 (34 stat points) and job level 6 (5 skill points), with AGI 15 and VIT 10 raised
    // for 34 points and Strike 1, First Aid 1, and Focus 3 learned, standing beside the NPC.
    private static (TestServer Server, ConnectionId Player) Beside(string npc, float distance = 2f)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, Character);
        server.Store.GiveItems(Character, Health, 1, 3, 1);
        server.SendEnterWorld(player, Character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        PlayerEntity entity = server.PlayerOf(player);
        entity.Level = 10;
        entity.SetPrimary(new PrimaryStats(5, 15, 10, 5, 5, 5));
        server.Stats.Recalculate(entity, server.Content.Jobs[entity.Job]);
        NpcEntity at = server.NpcOf(npc);
        server.Place(player, at.Position.X + distance, at.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();
        return (server, player);
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

    private static T Last<T>(TestServer server, ConnectionId player, MessageOpcode opcode, Func<byte[], T?> read)
        where T : class
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload))
            .Last(message => message != null)!;
    }

    [TestCase(GateWarden, 2f, CommandRejectionReason.InvalidTarget,
        TestName = "ResetBuild_AtAnNpcThatDoesNotOfferIt_IsRefused")]
    [TestCase(Guildmaster, 6f, CommandRejectionReason.OutOfRange, TestName = "ResetBuild_OutOfReach_IsRefused")]
    public void ResetBuild_ThatCannotBeMade_ChangesNothing(string npc, float distance, CommandRejectionReason expected)
    {
        (TestServer server, ConnectionId player) = Beside(npc, distance);
        Assert.That(server.NpcOf(Guildmaster).Services.OffersReset, Is.True, "the Guildmaster offers it");
        Assert.That(server.NpcOf(GateWarden).Services.OffersReset, Is.False, "the Gate Warden does not");

        server.SendResetBuild(player, server.NpcOf(npc).Id, 7);
        server.Tick();

        Assert.That(
            Rejections(server, player).Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(new[] { (7u, expected) }));
        Assert.That(server.PlayerOf(player).Primary, Is.EqualTo(new PrimaryStats(5, 15, 10, 5, 5, 5)));
        Assert.That(server.PlayerOf(player).Skills, Is.Not.Empty);
    }

    [Test]
    public void ResetBuild_AtTheGuildmaster_ReturnsEveryPoint_AndTheListTheSheetTheHealthAndACheckpointFollow()
    {
        (TestServer server, ConnectionId player) = Beside(Guildmaster);
        PlayerEntity entity = server.PlayerOf(player);
        int maxHealth = entity.MaxHealth;
        int before = server.Store.Checkpoints.Count;

        server.SendResetBuild(player, server.NpcOf(Guildmaster).Id, 1);
        server.Tick();

        SkillList list = Last(server, player, MessageOpcode.SkillList, payload =>
            SkillList.TryRead(payload, out SkillList? read) ? read : null);
        CharacterSheet sheet = Last(server, player, MessageOpcode.CharacterSheet, payload =>
            CharacterSheet.TryRead(payload, out CharacterSheet? read) ? read : null);
        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That(entity.Primary, Is.EqualTo(Start));
        Assert.That(entity.Skills, Is.Empty);
        Assert.That(list.Skills.Select(entry => entry.Level), Is.All.EqualTo((byte)0));
        Assert.That((sheet.StatPoints, sheet.SkillPoints), Is.EqualTo(((ushort)34, (byte)5)), "every point back");
        Assert.That(entity.MaxHealth, Is.LessThan(maxHealth), "VIT back to 5");
        Assert.That(
            server.Transport.ControlSentTo(player).Any(message => message.Opcode == MessageOpcode.CharacterHealth),
            Is.True,
            "the new maximum");
        (LogLevel level, EventId eventId, string _, IReadOnlyDictionary<string, object?> fields) reset =
            server.BuildsLog.Entries.Single();
        Assert.That((reset.level, reset.eventId.Id), Is.EqualTo((LogLevel.Information, 1014)));
        Assert.That(reset.fields["Reason"], Is.EqualTo(CharacterBuilds.GuildmasterReason));

        server.TickUntil(() => server.Store.Checkpoints.Count > before);
        CharacterCheckpoint checkpoint = server.Store.Checkpoints.Last();
        Assert.That((checkpoint.Stats, checkpoint.Skills!.Count), Is.EqualTo((Start, 0)), "queued at once");
    }

    [Test]
    public void ResetBuild_InterruptsACast_AndLeavesStatusEffectsToRunOut()
    {
        (TestServer server, ConnectionId player) = Beside(Guildmaster);
        PlayerEntity entity = server.PlayerOf(player);
        server.SendUseSkill(player, "skill.focus", default, 1);
        server.Tick(3);
        ActiveStatusEffect focus = entity.StatusEffects.Single();
        server.SendUseSkill(player, "skill.first_aid", default, 2);
        server.Tick();
        bool wasCasting = entity.Combat.IsCasting;

        server.SendResetBuild(player, server.NpcOf(Guildmaster).Id, 3);
        server.Tick();

        Assert.That(wasCasting, Is.True, "First Aid takes a while");
        Assert.That(entity.Combat.IsCasting, Is.False, "the cast ends with the reset");
        Assert.That(entity.StatusEffects.Single(), Is.EqualTo(focus), "Focus runs out as it was");
    }

    [Test]
    public void ResetBuild_OfAnNpcNobodyKnows_IsRefusedAsAnInvalidTarget()
    {
        (TestServer server, ConnectionId player) = Beside(Guildmaster);

        server.SendResetBuild(player, new EntityId(999999), 7);
        server.Tick();

        Assert.That(Rejections(server, player).Single().Reason, Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void ResetBuild_WhileAnItemActionIsInFlight_IsReason9()
    {
        (TestServer server, ConnectionId player) = Beside(Guildmaster);
        long row = server.SessionOf(player).Character!.Inventory.Rows.Single().InventoryItem;
        server.RunsPersistence = false;

        server.SendUseItem(player, row, 1);
        server.SendResetBuild(player, server.NpcOf(Guildmaster).Id, 2);
        server.Tick();

        Assert.That(
            Rejections(server, player).Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(new[] { (2u, CommandRejectionReason.ItemActionInFlight) }));
        Assert.That(server.PlayerOf(player).Skills, Is.Not.Empty);
        server.RunsPersistence = true;
    }

    [Test]
    public void ResetBuild_WhileDead_IsRefusedAsNotAllowedNow()
    {
        (TestServer server, ConnectionId player) = Beside(Guildmaster);
        server.Combat.Kill(server.World.Maps.Single(), server.PlayerOf(player), null, server.CurrentTick);

        server.SendResetBuild(player, server.NpcOf(Guildmaster).Id, 1);
        server.Tick();

        Assert.That(Rejections(server, player).Single().Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(server.PlayerOf(player).Skills, Is.Not.Empty);
    }

    [Test]
    public void ResetBuild_WithNothingSpent_StillQueuesItsCheckpoint()
    {
        var server = new TestServer(withNpcs: true, withAdventurerBuild: false);
        ConnectionId player = server.EnterWorld(Character);
        NpcEntity at = server.NpcOf(Guildmaster);
        server.Place(player, at.Position.X + 2f, at.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();
        int before = server.Store.Checkpoints.Count;

        server.SendResetBuild(player, at.Id, 1);
        server.Tick();
        server.TickUntil(() => server.Store.Checkpoints.Count > before);

        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That(server.Store.Checkpoints.Count, Is.GreaterThan(before));
    }
}
}
