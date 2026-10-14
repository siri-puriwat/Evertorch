using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class StatusEffectsMessageTests
{
    private static readonly byte[] StatusA = { 0x08, 0x00, 0x73, 0x74, 0x61, 0x74, 0x75, 0x73, 0x2E, 0x61 };

    private static readonly byte[] EffectsBytes = new byte[] { 0x1C, 0x80, 0x01 }
        .Concat(StatusA)
        .Concat(new byte[] { 0x60, 0xEA, 0x00, 0x00 })
        .ToArray();

    private static readonly StatusDefinitionId Status = new("status.a");

    private static StatusEffectEntry[] Entries(int count, int idLength)
    {
        return Enumerable.Range(0, count)
            .Select(index => new StatusEffectEntry(
                new StatusDefinitionId($"status.{(char)('a' + index)}{new string('x', idLength - 8)}"),
                uint.MaxValue))
            .ToArray();
    }

    [Test]
    public void StatusEffects_ForGoldenBytes_RoundTrips()
    {
        var message = new StatusEffects(new[] { new StatusEffectEntry(Status, 60_000) });

        byte[] written = new byte[message.GetEncodedLength()];
        int length = message.Write(written);
        bool isRead = StatusEffects.TryRead(EffectsBytes, out StatusEffects? read);

        Assert.That(length, Is.EqualTo(EffectsBytes.Length));
        Assert.That(written, Is.EqualTo(EffectsBytes));
        Assert.That(isRead, Is.True);
        StatusEffectEntry entry = read!.Effects.Single();
        Assert.That((entry.Status, entry.RemainingMs), Is.EqualTo((Status, 60_000u)));
        WireMatrix.AssertRejectsEveryTruncation(EffectsBytes, bytes => StatusEffects.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(EffectsBytes, bytes => StatusEffects.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(EffectsBytes, bytes => StatusEffects.TryRead(bytes, out _));
    }

    // Fifteen 64-byte IDs take 3 + 15 × 70 = 1,053 bytes: the constructor refuses them, and the reader refuses such a
    // payload by its length before anything else (Network Protocol §6).
    [Test]
    public void StatusEffects_PastItsBytes_IsRefusedByTheConstructorAndTheReader()
    {
        StatusEffectEntry[] entries = Entries(15, 64);
        Action tooLarge = () => _ = new StatusEffects(entries);
        var fits = new StatusEffects(entries.Take(14).ToArray());
        byte[] buffer = new byte[fits.GetEncodedLength() + 70];
        fits.Write(buffer);
        buffer[2] = 15;
        byte[] last = new byte[73];
        new StatusEffects(entries.Skip(14).ToArray()).Write(last);
        Array.Copy(last, 3, buffer, buffer.Length - 70, 70);

        Assert.That(tooLarge, Throws.ArgumentException);
        Assert.That(buffer.Length, Is.EqualTo(1053));
        Assert.That(StatusEffects.TryRead(buffer, out _), Is.False);
    }

    [Test]
    public void StatusEffects_WithNone_IsTheOpcodeAndAZeroCount()
    {
        var message = new StatusEffects(new StatusEffectEntry[0]);
        byte[] written = new byte[message.GetEncodedLength()];
        message.Write(written);

        Assert.That(written, Is.EqualTo(new byte[] { 0x1C, 0x80, 0x00 }));
        Assert.That(StatusEffects.TryRead(written, out StatusEffects? read), Is.True);
        Assert.That(read!.Effects, Is.Empty);
    }

    // Twenty-four entries of 36-byte IDs take 3 + 24 × 42 = 1,011 of the 1,020 bytes a reliable message carries.
    [Test]
    public void StatusEffects_WithTheMostEntriesWithinItsBytes_FitsOneReliableMessage()
    {
        var message = new StatusEffects(Entries(StatusEffects.MaxEntries, 36));
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        Assert.That(StatusEffects.TryRead(buffer, out StatusEffects? read), Is.True);
        Assert.That(read!.Effects, Has.Count.EqualTo(24));
        Assert.That(buffer.Length, Is.EqualTo(1011));
    }

    [Test]
    public void StatusEffects_WithTheSameEffectTwiceTooManyEntriesOrAnotherKindOfId_IsRefused()
    {
        byte[] entry = EffectsBytes.Skip(3).ToArray();
        byte[] twice = new byte[] { 0x1C, 0x80, 0x02 }.Concat(entry).Concat(entry).ToArray();
        byte[] tooMany = WireMatrix.With(EffectsBytes, 2, StatusEffects.MaxEntries + 1);
        byte[] skillId = WireMatrix.With(EffectsBytes, 5, 0x73, 0x6B, 0x69, 0x6C, 0x6C, 0x2E, 0x61, 0x61);

        Assert.That(StatusEffects.TryRead(twice, out _), Is.False);
        Assert.That(StatusEffects.TryRead(tooMany, out _), Is.False);
        Assert.That(StatusEffects.TryRead(skillId, out _), Is.False, "'skill.aa' is not a status effect");
    }
}
}
