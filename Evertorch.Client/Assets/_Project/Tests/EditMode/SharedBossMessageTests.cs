using System;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedBossMessageTests
{
    private static readonly byte[] BossFellBytes =
    {
        0x24, 0x80, 0x02,
        0x15, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x73, 0x6C, 0x69, 0x6D, 0x65, 0x5F, 0x6D, 0x6F,
        0x6E, 0x61, 0x72, 0x63, 0x68,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61
    };

    [Test]
    public void BossAnnouncement_WritesItsGoldenBytes_AndReadsThemBack()
    {
        var monarch = new MonsterDefinitionId("monster.slime_monarch");
        var fell = new BossAnnouncement(BossAnnouncementKind.Fell, monarch, "Anna");
        byte[] written = new byte[fell.GetEncodedLength()];
        fell.Write(written);

        Assert.That(written, Is.EqualTo(BossFellBytes));
        Assert.That(BossAnnouncement.TryRead(BossFellBytes, out BossAnnouncement? read), Is.True);
        Assert.That((read!.Kind, read.Monster, read.Name), Is.EqualTo((BossAnnouncementKind.Fell, monarch, "Anna")));
        Assert.That(BossAnnouncement.TryRead(BossFellBytes.AsSpan(0, BossFellBytes.Length - 1), out _), Is.False);
    }
}
}
