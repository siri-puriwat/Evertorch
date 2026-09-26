using System;
using System.Linq;
using System.Text;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class NpcServicesMessageTests
{
    // NPC 7; item.a sold for 20 and bought for 10; quest.a asks for 5 of monster.a for 150 experience and 100 coins.
    private static readonly byte[] ServicesBytes =
    {
        0x1D, 0x80, 0x07, 0, 0, 0, 0, 0, 0, 0, 0x01,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x14, 0, 0, 0, 0x0A, 0, 0, 0,
        0x01,
        0x07, 0x00, 0x71, 0x75, 0x65, 0x73, 0x74, 0x2E, 0x61,
        0x09, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x61,
        0x05, 0x00, 0x96, 0, 0, 0, 0, 0, 0, 0, 0x64, 0, 0, 0
    };

    // Offsets into the golden bytes.
    private const int EntryCount = 10;
    private const int BuyPrice = 19;
    private const int SellPrice = 23;
    private const int OfferCount = 27;
    private const int QuestId = 30;
    private const int MonsterId = 39;
    private const int Count = 48;
    private const int Experience = 50;
    private const int Coins = 58;

    private static NpcServices Golden =>
        new(
            new EntityId(7),
            new[] { new NpcServiceEntry(new ItemDefinitionId("item.a"), 20, 10) },
            new[]
            {
                new NpcQuestOffer(new QuestDefinitionId("quest.a"), new MonsterDefinitionId("monster.a"), 5, 150, 100)
            });

    private static string LongestId(string kind, int index)
    {
        return $"{kind}.{(char)('a' + index)}{new string('x', 62 - kind.Length)}";
    }

    private static byte[] Text(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return BitConverter.GetBytes((ushort)bytes.Length).Concat(bytes).ToArray();
    }

    // NPC 7 and the entry count.
    private static byte[] Header(int entries)
    {
        return new byte[] { 0x1D, 0x80, 0x07, 0, 0, 0, 0, 0, 0, 0, (byte)entries };
    }

    private static byte[] Entry(string item)
    {
        return Text(item).Concat(BitConverter.GetBytes(20u)).Concat(BitConverter.GetBytes(10u)).ToArray();
    }

    private static byte[] Offer(string quest, string monster)
    {
        return Text(quest)
            .Concat(Text(monster))
            .Concat(BitConverter.GetBytes((ushort)5))
            .Concat(BitConverter.GetBytes(150ul))
            .Concat(BitConverter.GetBytes(100u))
            .ToArray();
    }

    private static byte[] Services(string[] items, string[] quests, string monster)
    {
        return Header(items.Length)
            .Concat(items.SelectMany(Entry))
            .Concat(new[] { (byte)quests.Length })
            .Concat(quests.SelectMany(quest => Offer(quest, monster)))
            .ToArray();
    }

    [TestCase(BuyPrice, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, "an entry with neither price")]
    [TestCase(Count, new byte[] { 0, 0 }, "a count of 0")]
    [TestCase(Experience, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "an offer rewarding nothing")]
    [TestCase(13, new byte[] { 0x71, 0x75, 0x65, 0x73 }, "'ques.a' for an item")]
    [TestCase(QuestId, new byte[] { 0x69, 0x74, 0x65, 0x6D, 0x73 }, "'items.a' for a quest")]
    [TestCase(MonsterId, new byte[] { 0x71, 0x75, 0x65, 0x73, 0x74, 0x2E }, "'quest.r.a' for a monster")]
    public void NpcServices_WithAnImpossibleValue_IsRefused(int offset, byte[] replacement, string reason)
    {
        byte[] bytes = WireMatrix.With(ServicesBytes, offset, replacement);

        Assert.That(NpcServices.TryRead(bytes, out _), Is.False, reason);
    }

    [Test]
    public void NpcServices_AtTheirLargest_FitOneReliableMessage()
    {
        NpcServiceEntry[] entries = Enumerable.Range(0, NpcServices.MaxEntries)
            .Select(index => new NpcServiceEntry(new ItemDefinitionId(LongestId("item", index)), 1, 1))
            .ToArray();
        NpcQuestOffer[] offers = Enumerable.Range(0, NpcServices.MaxOffers)
            .Select(index => new NpcQuestOffer(
                new QuestDefinitionId(LongestId("quest", index)),
                new MonsterDefinitionId(LongestId("monster", index)),
                ushort.MaxValue,
                ulong.MaxValue,
                uint.MaxValue))
            .ToArray();
        var shop = new NpcServices(new EntityId(long.MaxValue), entries, new NpcQuestOffer[0]);
        var giver = new NpcServices(new EntityId(long.MaxValue), new NpcServiceEntry[0], offers);
        Action tooLarge = () => _ = new NpcServices(new EntityId(1), entries, offers.Take(1).ToArray());

        Assert.That(entries[0].Item.Value.Length, Is.EqualTo(64));
        Assert.That(offers[0].Monster.Value.Length, Is.EqualTo(64));
        Assert.That(shop.GetEncodedLength(), Is.EqualTo(12 + 13 * 74), "974 bytes");
        Assert.That(giver.GetEncodedLength(), Is.EqualTo(12 + 6 * 146), "888 bytes");
        Assert.That(tooLarge, Throws.ArgumentException, "1,120 bytes do not fit the 1,020 a reliable message carries");
        foreach (NpcServices message in new[] { shop, giver })
        {
            byte[] buffer = new byte[message.GetEncodedLength()];
            message.Write(buffer);
            Assert.That(NpcServices.TryRead(buffer, out NpcServices? read), Is.True);
            Assert.That(
                (read!.Entries.Count, read.Offers.Count),
                Is.EqualTo((message.Entries.Count, message.Offers.Count)));
        }
    }

    // Built by hand, since the constructor refuses to build them: the reader must refuse them, not throw.
    [Test]
    public void NpcServices_BeyondTheirLimits_AreRefusedByTheReader()
    {
        string[] fourteen = Enumerable.Range(0, NpcServices.MaxEntries + 1)
            .Select(index => $"item.{(char)('a' + index)}")
            .ToArray();
        string[] seven = Enumerable.Range(0, NpcServices.MaxOffers + 1)
            .Select(index => $"quest.{(char)('a' + index)}")
            .ToArray();
        string[] longest = Enumerable.Range(0, NpcServices.MaxEntries).Select(index => LongestId("item", index))
            .ToArray();
        byte[] tooLarge = Services(longest, new[] { LongestId("quest", 0) }, LongestId("monster", 0));
        string[] none = new string[0];

        Assert.That(NpcServices.TryRead(Services(fourteen.Take(13).ToArray(), none, "monster.a"), out _), Is.True);
        Assert.That(NpcServices.TryRead(Services(none, seven.Take(6).ToArray(), "monster.a"), out _), Is.True);
        Assert.That(NpcServices.TryRead(Services(fourteen, none, "monster.a"), out _), Is.False, "14 entries");
        Assert.That(NpcServices.TryRead(Services(none, seven, "monster.a"), out _), Is.False, "7 offers");
        Assert.That(tooLarge.Length, Is.EqualTo(1120));
        Assert.That(NpcServices.TryRead(tooLarge, out _), Is.False, "1,120 bytes");
    }

    [Test]
    public void NpcServices_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[Golden.GetEncodedLength()];
        int length = Golden.Write(written);
        bool isRead = NpcServices.TryRead(ServicesBytes, out NpcServices? read);

        Assert.That(length, Is.EqualTo(ServicesBytes.Length));
        Assert.That(written, Is.EqualTo(ServicesBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Npc, Is.EqualTo(new EntityId(7)));
        NpcServiceEntry entry = read.Entries.Single();
        Assert.That((entry.Item.Value, entry.BuyPrice, entry.SellPrice), Is.EqualTo(("item.a", 20u, 10u)));
        NpcQuestOffer offer = read.Offers.Single();
        Assert.That(
            (offer.Quest.Value, offer.Monster.Value, offer.Count, offer.BaseExperience, offer.Coins),
            Is.EqualTo(("quest.a", "monster.a", (ushort)5, 150ul, 100u)));
        WireMatrix.AssertRejectsEveryTruncation(ServicesBytes, bytes => NpcServices.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(ServicesBytes, bytes => NpcServices.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(ServicesBytes, bytes => NpcServices.TryRead(bytes, out _));
    }

    [Test]
    public void NpcServices_GoldenOffsets_PointAtTheirFields()
    {
        Assert.That(ServicesBytes[EntryCount], Is.EqualTo(1));
        Assert.That(BitConverter.ToUInt32(ServicesBytes, BuyPrice), Is.EqualTo(20u));
        Assert.That(BitConverter.ToUInt32(ServicesBytes, SellPrice), Is.EqualTo(10u));
        Assert.That(ServicesBytes[OfferCount], Is.EqualTo(1));
        Assert.That(BitConverter.ToUInt16(ServicesBytes, Count), Is.EqualTo(5));
        Assert.That(BitConverter.ToUInt64(ServicesBytes, Experience), Is.EqualTo(150ul));
        Assert.That(BitConverter.ToUInt32(ServicesBytes, Coins), Is.EqualTo(100u));
    }

    [Test]
    public void NpcServices_WithAnItemOrAQuestTwice_IsRefused()
    {
        byte[] entry = ServicesBytes.Skip(11).Take(16).ToArray();
        byte[] offer = ServicesBytes.Skip(28).ToArray();
        byte[] header = ServicesBytes.Take(10).ToArray();
        byte[] itemTwice = header.Concat(new byte[] { 0x02 }).Concat(entry).Concat(entry).Concat(new byte[] { 0x00 })
            .ToArray();
        byte[] questTwice = header.Concat(new byte[] { 0x00, 0x02 }).Concat(offer).Concat(offer).ToArray();
        byte[] once = header.Concat(new byte[] { 0x01 }).Concat(entry).Concat(new byte[] { 0x00 }).ToArray();

        Assert.That(NpcServices.TryRead(once, out _), Is.True, "the pieces read alone");
        Assert.That(NpcServices.TryRead(itemTwice, out _), Is.False, "the same item twice");
        Assert.That(NpcServices.TryRead(questTwice, out _), Is.False, "the same quest twice");
    }

    [Test]
    public void NpcServices_WithNothing_IsTheHeaderAlone()
    {
        var message = new NpcServices(new EntityId(7), new NpcServiceEntry[0], new NpcQuestOffer[0]);
        byte[] written = new byte[message.GetEncodedLength()];
        message.Write(written);

        Assert.That(written, Is.EqualTo(new byte[] { 0x1D, 0x80, 0x07, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x00 }));
        Assert.That(NpcServices.TryRead(written, out NpcServices? read), Is.True);
        Assert.That((read!.Entries.Count, read.Offers.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void NpcServices_WithTooManyEntriesOrOffers_CannotBeBuilt()
    {
        NpcServiceEntry[] entries = Enumerable.Range(0, NpcServices.MaxEntries + 1)
            .Select(index => new NpcServiceEntry(new ItemDefinitionId($"item.{(char)('a' + index)}"), 1, 1))
            .ToArray();
        NpcQuestOffer[] offers = Enumerable.Range(0, NpcServices.MaxOffers + 1)
            .Select(index => new NpcQuestOffer(
                new QuestDefinitionId($"quest.{(char)('a' + index)}"),
                new MonsterDefinitionId("monster.a"),
                1,
                1,
                1))
            .ToArray();
        Action tooManyEntries = () => _ = new NpcServices(new EntityId(1), entries, new NpcQuestOffer[0]);
        Action tooManyOffers = () => _ = new NpcServices(new EntityId(1), new NpcServiceEntry[0], offers);

        Assert.That(tooManyEntries, Throws.ArgumentException);
        Assert.That(tooManyOffers, Throws.ArgumentException);
    }
}
}
