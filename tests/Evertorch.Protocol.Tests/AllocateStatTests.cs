using System;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class AllocateStatTests
{
    // AGI raised 3 times, command sequence 0x0A0B0C0D.
    private static readonly byte[] AllocateBytes = { 0x17, 0x00, 0x02, 0x03, 0x0D, 0x0C, 0x0B, 0x0A };

    private static bool Read(byte[] bytes)
    {
        return AllocateStat.TryRead(bytes, out _);
    }

    [TestCase((byte)0)]
    [TestCase((byte)7)]
    [TestCase((byte)255)]
    public void AllocateStat_ForAStatisticOutsideStrToLuk_IsRefused(byte stat)
    {
        Assert.That(Read(WireMatrix.With(AllocateBytes, 2, stat)), Is.False);
    }

    [TestCase((byte)1)]
    [TestCase((byte)6)]
    public void AllocateStat_ForStrAndLuk_IsRead(byte stat)
    {
        Assert.That(AllocateStat.TryRead(WireMatrix.With(AllocateBytes, 2, stat), out AllocateStat read), Is.True);
        Assert.That((byte)read.Stat, Is.EqualTo(stat));
    }

    [Test]
    public void AllocateStat_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[AllocateStat.EncodedLength];

        int length = new AllocateStat(PrimaryStat.Agi, 3, 0x0A0B0C0D).Write(written);
        bool isRead = AllocateStat.TryRead(AllocateBytes, out AllocateStat read);

        Assert.That(length, Is.EqualTo(8));
        Assert.That(written, Is.EqualTo(AllocateBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read.Stat, read.Steps, read.CommandSequence), Is.EqualTo((PrimaryStat.Agi, (byte)3, 0x0A0B0C0Du)));
    }

    [Test]
    public void AllocateStat_OfNoSteps_IsRefused_AndOf255_IsRead()
    {
        Assert.That(Read(WireMatrix.With(AllocateBytes, 3, 0)), Is.False);
        Assert.That(AllocateStat.TryRead(WireMatrix.With(AllocateBytes, 3, 255), out AllocateStat read), Is.True);
        Assert.That(read.Steps, Is.EqualTo((byte)255));
    }

    [Test]
    public void AllocateStat_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(AllocateBytes, Read);
        WireMatrix.AssertRejectsTrailingData(AllocateBytes, Read);
        WireMatrix.AssertRejectsOtherOpcodes(AllocateBytes, Read);
    }

    [Test]
    public void PrimaryStat_Values_KeepTheirWireNumbers_InTheSheetsOrder()
    {
        Assert.That(
            Array.ConvertAll((PrimaryStat[])Enum.GetValues(typeof(PrimaryStat)), stat => (byte)stat),
            Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5, 6 }));
        Assert.That(
            Enum.GetNames(typeof(PrimaryStat)),
            Is.EqualTo(new[] { "None", "Str", "Agi", "Vit", "Int", "Dex", "Luk" }));
    }
}
}
