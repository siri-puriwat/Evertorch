using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the skill and status messages, so both compilers and runtimes agree on the wire
///     format.
/// </summary>
[TestFixture]
public sealed class SharedSkillMessageTests
{
    // Pieces of messages, not messages: a name ending in "Bytes" would make SharedMirrorCoverageTests count them as
    // mirrors of CancelAction (07 00) and UseSkill (08 00).
    private static readonly byte[] SkillA = { 0x07, 0x00, 0x73, 0x6B, 0x69, 0x6C, 0x6C, 0x2E, 0x61 };

    private static readonly byte[] StatusA = { 0x08, 0x00, 0x73, 0x74, 0x61, 0x74, 0x75, 0x73, 0x2E, 0x61 };

    private static readonly byte[] UseSkillBytes = Concat(
        new byte[] { 0x08, 0x00 },
        SkillA,
        new byte[] { 0x2A, 0, 0, 0, 0, 0, 0, 0, 0x78, 0x56, 0x34, 0x12 });

    private static readonly byte[] CastStartedBytes = Concat(
        new byte[] { 0x0B, 0x80, 0x01, 0, 0, 0, 0, 0, 0, 0 },
        SkillA,
        new byte[] { 0x2A, 0, 0, 0, 0, 0, 0, 0, 0x10, 0, 0, 0, 0x33, 0x05, 0, 0 });

    private static readonly byte[] ResolvedBytes = Concat(
        new byte[] { 0x0C, 0x80, 0x01, 0, 0, 0, 0, 0, 0, 0, 0x2A, 0, 0, 0, 0, 0, 0, 0 },
        SkillA,
        new byte[] { 0x01, 0x11, 0, 0, 0, 0x20, 0, 0, 0, 0x94, 0x02 });

    // skill.a at level 2 of 5, with no prerequisite.
    private static readonly byte[] ListBytes = Concat(
        new byte[] { 0x1B, 0x80, 0x01 },
        SkillA,
        new byte[]
        {
            0x00, 0x00, 0xC0, 0x3F, 0x08, 0, 0, 0, 0xD0, 0x07, 0, 0, 0xF4, 0x01, 0, 0, 0xE2, 0x04, 0, 0,
            0x02, 0x05, 0xFF, 0x00
        });

    private static readonly byte[] LearnBytes =
        Concat(new byte[] { 0x18, 0x00 }, SkillA, new byte[] { 0x78, 0x56, 0x34, 0x12 });

    private static readonly byte[] EffectsBytes = Concat(
        new byte[] { 0x1C, 0x80, 0x01 },
        StatusA,
        new byte[] { 0x60, 0xEA, 0x00, 0x00 });

    private static readonly byte[] NoEffectsBytes = { 0x1C, 0x80, 0x00 };

    private static readonly SkillDefinitionId Skill = new("skill.a");

    private static readonly StatusDefinitionId Status = new("status.a");

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(part => part).ToArray();
    }

    [Test]
    public void LearnSkill_WriteAndRead_MatchGoldenBytes()
    {
        var message = new LearnSkill(Skill, 0x12345678);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = LearnSkill.TryRead(LearnBytes, out LearnSkill? read);

        Assert.That(buffer, Is.EqualTo(LearnBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read!.Skill, read.CommandSequence), Is.EqualTo((Skill, 0x12345678u)));
    }

    [Test]
    public void MessageRouting_ForSkillMessages_UsesTheControlChannel()
    {
        foreach (MessageOpcode opcode in new[]
                 {
                     MessageOpcode.UseSkill, MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved,
                     MessageOpcode.SkillList, MessageOpcode.StatusEffects
                 })
        {
            Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery));
            Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
            Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        }
    }

    [Test]
    public void SkillCastStarted_WriteAndRead_MatchGoldenBytes()
    {
        var message = new SkillCastStarted(new EntityId(1), Skill, new EntityId(42), 16, 1331);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = SkillCastStarted.TryRead(CastStartedBytes, out SkillCastStarted? read);

        Assert.That(buffer, Is.EqualTo(CastStartedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Caster, Is.EqualTo(new EntityId(1)));
        Assert.That(read.Skill, Is.EqualTo(Skill));
        Assert.That(read.Target, Is.EqualTo(new EntityId(42)));
        Assert.That(read.StartTick, Is.EqualTo(16u));
        Assert.That(read.CastMs, Is.EqualTo(1331u));
    }

    [Test]
    public void SkillList_AtItsLargest_Is993Bytes()
    {
        SkillListEntry[] entries = Enumerable.Range(0, SkillList.MaxEntries)
            .Select(index => new SkillListEntry(
                new SkillDefinitionId($"skill.{(char)('a' + index)}{new string('x', 57)}"),
                6f,
                uint.MaxValue,
                uint.MaxValue,
                uint.MaxValue,
                uint.MaxValue,
                5,
                5,
                SkillListEntry.NoPrerequisite,
                0))
            .ToArray();

        Assert.That(new SkillList(entries).GetEncodedLength(), Is.EqualTo(993));
    }

    [Test]
    public void SkillList_WriteAndRead_MatchGoldenBytes()
    {
        var message = new SkillList(
            new[] { new SkillListEntry(Skill, 1.5f, 8, 2000, 500, 1250, 2, 5, SkillListEntry.NoPrerequisite, 0) });
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = SkillList.TryRead(ListBytes, out SkillList? read);

        Assert.That(buffer, Is.EqualTo(ListBytes));
        Assert.That(isRead, Is.True);
        SkillListEntry entry = read!.Skills.Single();
        Assert.That(entry.Skill, Is.EqualTo(Skill));
        Assert.That(entry.Range, Is.EqualTo(1.5f));
        Assert.That(entry.SpCost, Is.EqualTo(8u));
        Assert.That(entry.CooldownMs, Is.EqualTo(2000u));
        Assert.That(entry.AfterCastDelayMs, Is.EqualTo(500u));
        Assert.That(entry.RemainingCooldownMs, Is.EqualTo(1250u));
        Assert.That(
            (entry.Level, entry.MaxLevel, entry.PrerequisiteIndex, entry.PrerequisiteLevel),
            Is.EqualTo(((byte)2, (byte)5, SkillListEntry.NoPrerequisite, (byte)0)));
    }

    [Test]
    public void SkillResolved_WriteAndRead_MatchGoldenBytes()
    {
        var message = new SkillResolved(new EntityId(1), new EntityId(42), Skill, SkillOutcome.Hit, 17, 32, 660);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = SkillResolved.TryRead(ResolvedBytes, out SkillResolved? read);

        Assert.That(buffer, Is.EqualTo(ResolvedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Caster, Is.EqualTo(new EntityId(1)));
        Assert.That(read.Target, Is.EqualTo(new EntityId(42)));
        Assert.That(read.Skill, Is.EqualTo(Skill));
        Assert.That(read.Outcome, Is.EqualTo(SkillOutcome.Hit));
        Assert.That(read.Amount, Is.EqualTo(17u));
        Assert.That(read.ServerTick, Is.EqualTo(32u));
        Assert.That(read.TargetHealthPermille, Is.EqualTo(660));
    }

    [Test]
    public void StatusEffects_AtTheirLargest_Are983Bytes()
    {
        StatusEffectEntry[] entries = Enumerable.Range(0, StatusEffects.MaxEntries)
            .Select(index => new StatusEffectEntry(
                new StatusDefinitionId($"status.{(char)('a' + index)}{new string('x', 56)}"),
                uint.MaxValue))
            .ToArray();

        Assert.That(new StatusEffects(entries).GetEncodedLength(), Is.EqualTo(983));
    }

    [Test]
    public void StatusEffects_WithNone_MatchGoldenBytes()
    {
        var message = new StatusEffects(new StatusEffectEntry[0]);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = StatusEffects.TryRead(NoEffectsBytes, out StatusEffects? read);

        Assert.That(buffer, Is.EqualTo(NoEffectsBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Effects, Is.Empty);
    }

    [Test]
    public void StatusEffects_WriteAndRead_MatchGoldenBytes()
    {
        var message = new StatusEffects(new[] { new StatusEffectEntry(Status, 60_000) });
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = StatusEffects.TryRead(EffectsBytes, out StatusEffects? read);

        Assert.That(buffer, Is.EqualTo(EffectsBytes));
        Assert.That(isRead, Is.True);
        StatusEffectEntry entry = read!.Effects.Single();
        Assert.That(entry.Status, Is.EqualTo(Status));
        Assert.That(entry.RemainingMs, Is.EqualTo(60_000u));
    }

    [Test]
    public void UseSkill_WriteAndRead_MatchGoldenBytes()
    {
        var message = new UseSkill(Skill, new EntityId(42), 0x12345678);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = UseSkill.TryRead(UseSkillBytes, out UseSkill? read);

        Assert.That(buffer, Is.EqualTo(UseSkillBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Skill, Is.EqualTo(Skill));
        Assert.That(read.Target, Is.EqualTo(new EntityId(42)));
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
    }
}
}
