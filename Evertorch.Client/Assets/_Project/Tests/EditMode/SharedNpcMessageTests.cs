using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the NPC messages, so both compilers and runtimes agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedNpcMessageTests
{
    // NPC 7; item.a sold for 20 and bought for 10; quest.a asks for 5 of monster.a for 150 experience, 160 job
    // experience, and 100 coins; the reset offered; no job change.
    private static readonly byte[] ServicesBytes =
    {
        0x1D, 0x80, 0x07, 0, 0, 0, 0, 0, 0, 0, 0x01,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x14, 0, 0, 0, 0x0A, 0, 0, 0,
        0x01,
        0x07, 0x00, 0x71, 0x75, 0x65, 0x73, 0x74, 0x2E, 0x61,
        0x09, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x61,
        0x05, 0x00, 0x96, 0, 0, 0, 0, 0, 0, 0, 0xA0, 0, 0, 0, 0, 0, 0, 0, 0x64, 0, 0, 0,
        0x01,
        0x00
    };

    // NPC 7, a Guildmaster: nothing traded or given; the reset; job.b from job.a at level 10.
    private static readonly byte[] GuildmasterBytes =
    {
        0x1D, 0x80, 0x07, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x00, 0x01, 0x01,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x62,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x61,
        0x0A, 0x00
    };

    // NPC 15, job.vanguard, command sequence 0x0A0B0C0D.
    private static readonly byte[] ChangeBytes =
    {
        0x1A, 0x00, 0x0F, 0, 0, 0, 0, 0, 0, 0,
        0x0C, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x76, 0x61, 0x6E, 0x67, 0x75, 0x61, 0x72, 0x64,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    // NPC 15, command sequence 0x0A0B0C0D.
    private static readonly byte[] ResetBytes = { 0x19, 0x00, 0x0F, 0, 0, 0, 0, 0, 0, 0, 0x0D, 0x0C, 0x0B, 0x0A };

    [Test]
    public void ChangeJob_WriteAndRead_MatchGoldenBytes()
    {
        var message = new ChangeJob(new EntityId(15), new JobDefinitionId("job.vanguard"), 0x0A0B0C0D);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = ChangeJob.TryRead(ChangeBytes, out ChangeJob? read);

        Assert.That(buffer, Is.EqualTo(ChangeBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Npc, Is.EqualTo(new EntityId(15)));
        Assert.That(read.Job.Value, Is.EqualTo("job.vanguard"));
        Assert.That(read.CommandSequence, Is.EqualTo(0x0A0B0C0Du));
        Assert.That(
            MessageRouting.TryGetRoute(MessageOpcode.ChangeJob, out ProtocolChannel channel, out MessageDelivery _),
            Is.True);
        Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
    }

    [Test]
    public void EntitySpawn_OfAnNpc_CarriesItsNpcId()
    {
        var spawn = new EntitySpawn(
            new EntityId(13),
            EntityKind.Npc,
            "npc.quartermaster",
            new WorldPosition(-3.5f, 0f, 4.5f),
            new WorldDirection(0.6f, -0.8f),
            EntityStateFlags.None,
            0);
        byte[] buffer = new byte[spawn.GetEncodedLength()];
        spawn.Write(buffer);

        Assert.That(buffer[10], Is.EqualTo((byte)EntityKind.Npc));
        Assert.That(EntitySpawn.TryRead(buffer, out EntitySpawn? read), Is.True);
        Assert.That(read!.Kind, Is.EqualTo(EntityKind.Npc));
        Assert.That(read.DefinitionId, Is.EqualTo("npc.quartermaster"));
    }

    [Test]
    public void NpcServices_OfAGuildmaster_WriteAndRead_MatchGoldenBytes()
    {
        var message = new NpcServices(
            new EntityId(7),
            new NpcServiceEntry[0],
            new NpcQuestOffer[0],
            true,
            new[] { new NpcJobChangeOffer(new JobDefinitionId("job.b"), new JobDefinitionId("job.a"), 10) });
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = NpcServices.TryRead(GuildmasterBytes, out NpcServices? read);

        Assert.That(buffer, Is.EqualTo(GuildmasterBytes));
        Assert.That(isRead, Is.True);
        NpcJobChangeOffer change = read!.JobChanges.Single();
        Assert.That(change.Job.Value, Is.EqualTo("job.b"));
        Assert.That(change.FromJob.Value, Is.EqualTo("job.a"));
        Assert.That(change.Level, Is.EqualTo((ushort)10));
    }

    [Test]
    public void NpcServices_RouteOnTheControlChannel()
    {
        Assert.That(
            MessageRouting.TryGetRoute(
                MessageOpcode.NpcServices,
                out ProtocolChannel channel,
                out MessageDelivery delivery));
        Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
    }

    [Test]
    public void NpcServices_WriteAndRead_MatchGoldenBytes()
    {
        var message = new NpcServices(
            new EntityId(7),
            new[] { new NpcServiceEntry(new ItemDefinitionId("item.a"), 20, 10) },
            new[]
            {
                new NpcQuestOffer(
                    new QuestDefinitionId("quest.a"),
                    new MonsterDefinitionId("monster.a"),
                    5,
                    150,
                    160,
                    100)
            },
            true);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = NpcServices.TryRead(ServicesBytes, out NpcServices? read);

        Assert.That(buffer, Is.EqualTo(ServicesBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Npc, Is.EqualTo(new EntityId(7)));
        NpcServiceEntry entry = read.Entries.Single();
        Assert.That(entry.Item.Value, Is.EqualTo("item.a"));
        Assert.That(entry.BuyPrice, Is.EqualTo(20u));
        Assert.That(entry.SellPrice, Is.EqualTo(10u));
        NpcQuestOffer offer = read.Offers.Single();
        Assert.That(offer.Quest.Value, Is.EqualTo("quest.a"));
        Assert.That(offer.Monster.Value, Is.EqualTo("monster.a"));
        Assert.That(offer.Count, Is.EqualTo((ushort)5));
        Assert.That(offer.BaseExperience, Is.EqualTo(150ul));
        Assert.That(offer.JobExperience, Is.EqualTo(160ul));
        Assert.That(offer.Coins, Is.EqualTo(100u));
        Assert.That(read.OffersReset, Is.True);
        Assert.That(read.JobChanges, Is.Empty);
    }

    [Test]
    public void ResetBuild_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[ResetBuild.EncodedLength];
        new ResetBuild(new EntityId(15), 0x0A0B0C0D).Write(buffer);

        bool isRead = ResetBuild.TryRead(ResetBytes, out ResetBuild read);

        Assert.That(buffer, Is.EqualTo(ResetBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read.Npc, read.CommandSequence), Is.EqualTo((new EntityId(15), 0x0A0B0C0Du)));
    }
}
}
