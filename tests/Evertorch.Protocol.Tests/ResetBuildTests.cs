using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ResetBuildTests
{
    // NPC 15, command sequence 0x0A0B0C0D.
    private static readonly byte[] ResetBytes = { 0x19, 0x00, 0x0F, 0, 0, 0, 0, 0, 0, 0, 0x0D, 0x0C, 0x0B, 0x0A };

    private static bool Read(byte[] bytes)
    {
        return ResetBuild.TryRead(bytes, out _);
    }

    [Test]
    public void ResetBuild_AtNoNpc_IsRefused_AndWhenMalformed()
    {
        Assert.That(Read(WireMatrix.With(ResetBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0)), Is.False, "NPC 0");
        Assert.That(Read(WireMatrix.With(ResetBytes, 2, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)), Is.False,
            "NPC -1");
        WireMatrix.AssertRejectsEveryTruncation(ResetBytes, Read);
        WireMatrix.AssertRejectsTrailingData(ResetBytes, Read);
        WireMatrix.AssertRejectsOtherOpcodes(ResetBytes, Read);
    }

    [Test]
    public void ResetBuild_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[ResetBuild.EncodedLength];

        int length = new ResetBuild(new EntityId(15), 0x0A0B0C0D).Write(written);
        bool isRead = ResetBuild.TryRead(ResetBytes, out ResetBuild read);

        Assert.That(length, Is.EqualTo(14));
        Assert.That(written, Is.EqualTo(ResetBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read.Npc, read.CommandSequence), Is.EqualTo((new EntityId(15), 0x0A0B0C0Du)));
    }
}
}
