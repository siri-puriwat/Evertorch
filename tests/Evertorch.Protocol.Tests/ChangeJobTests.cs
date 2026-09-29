using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ChangeJobTests
{
    // NPC 15, job.vanguard, command sequence 0x0A0B0C0D.
    private static readonly byte[] ChangeBytes =
    {
        0x1A, 0x00, 0x0F, 0, 0, 0, 0, 0, 0, 0,
        0x0C, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x76, 0x61, 0x6E, 0x67, 0x75, 0x61, 0x72, 0x64,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    // Offsets into the golden bytes.
    private const int Job = 12;

    private static bool Read(byte[] bytes)
    {
        return ChangeJob.TryRead(bytes, out _);
    }

    [Test]
    public void ChangeJob_AtNoNpcOrToNoJob_CannotBeBuilt()
    {
        Action noNpc = () => _ = new ChangeJob(default, new JobDefinitionId("job.vanguard"), 1);
        Action noJob = () => _ = new ChangeJob(new EntityId(15), default, 1);

        Assert.That(noNpc, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(noJob, Throws.ArgumentException);
    }

    [Test]
    public void ChangeJob_AtNoNpcOrToNoJob_IsRefused_AndWhenMalformed()
    {
        Assert.That(Read(WireMatrix.With(ChangeBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0)), Is.False, "NPC 0");
        Assert.That(Read(WireMatrix.With(ChangeBytes, 2, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)), Is.False,
            "NPC -1");
        Assert.That(Read(WireMatrix.With(ChangeBytes, Job, 0x4A)), Is.False, "a job ID with a capital");
        Assert.That(Read(WireMatrix.With(ChangeBytes, Job, 0x73, 0x6B, 0x69)), Is.False, "an ID of another kind");
        WireMatrix.AssertRejectsEveryTruncation(ChangeBytes, Read);
        WireMatrix.AssertRejectsTrailingData(ChangeBytes, Read);
        WireMatrix.AssertRejectsOtherOpcodes(ChangeBytes, Read);
    }

    [Test]
    public void ChangeJob_ForGoldenBytes_RoundTrips()
    {
        var message = new ChangeJob(new EntityId(15), new JobDefinitionId("job.vanguard"), 0x0A0B0C0D);
        byte[] written = new byte[message.GetEncodedLength()];

        int length = message.Write(written);
        bool isRead = ChangeJob.TryRead(ChangeBytes, out ChangeJob? read);

        Assert.That(length, Is.EqualTo(28));
        Assert.That(written, Is.EqualTo(ChangeBytes));
        Assert.That(isRead, Is.True);
        Assert.That(
            (read!.Npc, read.Job.Value, read.CommandSequence),
            Is.EqualTo((new EntityId(15), "job.vanguard", 0x0A0B0C0Du)));
    }
}
}
