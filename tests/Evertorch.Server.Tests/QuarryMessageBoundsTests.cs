using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Every bounded message the Ashen Quarry's content reaches, built from the repository's content at the largest
///     values its fields take, fits one reliable message and reads back as written (Network Protocol §6; Milestone 21
///     line 5): the Gate Warden's five offers, a log of every quest, the Smith's seventeen fees in two parts, the board's
///     thirty-six goods in three, a full bag, a trade's side, and a full storage of rows carrying the quarry's cards, an
///     inspection of eight pieces carrying them, and the Furnace Heart's casts, announcements, and award.
/// </summary>
[TestFixture]
public sealed class QuarryMessageBoundsTests
{
    private static readonly Lazy<ServerContent> Repository =
        new(() => ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage()));

    private static readonly EntityId Largest = new(long.MaxValue);
    private static readonly MonsterDefinitionId FurnaceHeart = new("monster.furnace_heart");

    // The quarry's cards, the longest ID first.
    private static readonly ItemDefinitionId[] QuarryCards =
    {
        new("item.card.basalt_crawler"), new("item.card.cinder_crawler"), new("item.card.furnace_heart"),
        new("item.card.ash_wisp")
    };

    private static ServerContent Content => Repository.Value;

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        Assert.That(length, Is.LessThanOrEqualTo(ProtocolLimits.MaxReliableMessageBytes), "one reliable message");
        return bytes;
    }

    // Of the pieces the filter takes, the one with the most slots and then the longest ID, a quarry card in each slot.
    private static (ItemDefinitionId Piece, ItemDefinitionId[] Cards) Carded(Func<ItemDefinition, bool> filter)
    {
        ItemDefinition piece = Content.Items.Values
            .Where(item => item.IsEquipment && filter(item))
            .OrderByDescending(item => item.Slots)
            .ThenByDescending(item => item.Id.Value.Length)
            .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
            .First();
        return (piece.Id, QuarryCards.Take(piece.Slots).ToArray());
    }

    private static string Row(ItemDefinitionId item, byte level, IReadOnlyList<ItemDefinitionId> cards)
    {
        return $"{item.Value}+{level}[{string.Join(",", cards.Select(card => card.Value))}]";
    }

    [Test]
    public void ALogOfEveryQuest_AtTheLargestProgress_FitsOneMessage()
    {
        QuestLogEntry[] entries = Content.Quests.Keys
            .OrderBy(quest => quest.Value, StringComparer.Ordinal)
            .Select(quest => new QuestLogEntry(quest, QuestState.Active, ushort.MaxValue, ushort.MaxValue))
            .ToArray();
        var log = new QuestLog(entries);

        byte[] written = Encode(log.GetEncodedLength(), bytes => log.Write(bytes));

        Assert.That(entries, Has.Length.EqualTo(5));
        Assert.That(QuestLog.TryRead(written, out QuestLog? read), Is.True);
        Assert.That(read!.Entries.Select(entry => entry.Quest), Is.EqualTo(entries.Select(entry => entry.Quest)));
    }

    // An inspection of the eight positions, each the most-slotted piece of its position refined to the top and carded
    // with the quarry's cards.
    [Test]
    public void AnInspection_OfEightPiecesCarryingTheQuarrysCards_ReadsBackInItsParts()
    {
        InspectedEntry[] worn = EquipmentPositions.All
            .Select(slot =>
            {
                (ItemDefinitionId piece, ItemDefinitionId[] cards) =
                    Carded(item => EquipmentPositions.Fits(item.Type, slot));
                return new InspectedEntry(slot, new InspectedItem(piece, byte.MaxValue, cards));
            })
            .ToArray();

        var read = new List<string>();
        foreach (InspectedEquipment part in InspectedEquipment.CreateParts(Largest, uint.MaxValue, uint.MaxValue, worn))
        {
            byte[] written = Encode(part.GetEncodedLength(), bytes => part.Write(bytes));
            Assert.That(InspectedEquipment.TryRead(written, out InspectedEquipment? back), Is.True);
            read.AddRange(back!.Entries.Select(entry =>
                $"{entry.Slot} {Row(entry.Item.Item, entry.Item.RefineLevel, entry.Item.Cards)}"));
        }

        Assert.That(
            read,
            Is.EqualTo(worn.Select(entry =>
                $"{entry.Slot} {Row(entry.Item.Item, entry.Item.RefineLevel, entry.Item.Cards)}")));
    }

    // A hundred rows in the bag, a side of ten, and three hundred rows in storage, each the most-slotted piece refined
    // to the top and carded with the quarry's cards.
    [Test]
    public void RowsCarryingTheQuarrysCards_FillTheBagATradeAndStorage_InPartsThatReadBack()
    {
        (ItemDefinitionId piece, ItemDefinitionId[] cards) = Carded(_ => true);
        string expected = Row(piece, byte.MaxValue, cards);
        InventoryEntry[] bag = Enumerable.Range(1, PickupSystem.MaxInventoryRows)
            .Select(row => new InventoryEntry(long.MaxValue - row, piece, 1, EquipmentSlot.None, byte.MaxValue, cards))
            .ToArray();
        TradeEntry[] side = Enumerable.Range(1, TradeSide.MaxEntries)
            .Select(row => new TradeEntry(long.MaxValue - row, piece, 1, byte.MaxValue, cards))
            .ToArray();
        StorageEntry[] storage = Enumerable.Range(1, ItemActionSystem.MaxStorageRows)
            .Select(row => new StorageEntry(long.MaxValue - row, piece, 1, byte.MaxValue, cards))
            .ToArray();

        var rows = new List<string>();
        foreach (InventorySnapshot part in InventorySnapshot.CreateParts(uint.MaxValue, ContentLimits.MaxCurrency, bag))
        {
            byte[] written = Encode(part.GetEncodedLength(), bytes => part.Write(bytes));
            Assert.That(InventorySnapshot.TryRead(written, out InventorySnapshot? back), Is.True);
            rows.AddRange(back!.Entries.Select(entry => Row(entry.Item, entry.RefineLevel, entry.Cards)));
        }

        foreach (TradeSide part in TradeSide.CreateParts(
                     TradeSideOwner.Own,
                     true,
                     true,
                     ContentLimits.MaxCurrency,
                     side))
        {
            byte[] written = Encode(part.GetEncodedLength(), bytes => part.Write(bytes));
            Assert.That(TradeSide.TryRead(written, out TradeSide? back), Is.True);
            rows.AddRange(back!.Entries.Select(entry => Row(entry.Item, entry.RefineLevel, entry.Cards)));
        }

        foreach (StorageSnapshot part in StorageSnapshot.CreateParts(
                     uint.MaxValue,
                     ContentLimits.MaxDepositFee,
                     storage))
        {
            byte[] written = Encode(part.GetEncodedLength(), bytes => part.Write(bytes));
            Assert.That(StorageSnapshot.TryRead(written, out StorageSnapshot? back), Is.True);
            rows.AddRange(back!.Entries.Select(entry => Row(entry.Item, entry.RefineLevel, entry.Cards)));
        }

        Assert.That(cards, Is.Not.Empty, "the piece holds cards");
        Assert.That(rows, Has.Count.EqualTo(PickupSystem.MaxInventoryRows + TradeSide.MaxEntries
            + ItemActionSystem.MaxStorageRows));
        Assert.That(rows, Is.All.EqualTo(expected));
    }

    // Network Protocol §6: each good at its ceiling and the largest stock, thirteen to a message.
    [Test]
    public void TheBoard_OfThirtySixGoods_TakesThreeMessages_EachReadBack()
    {
        MarketBoardEntry[] goods = Content.Items.Values
            .Where(item => item.Market != null)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select(item => new MarketBoardEntry(item.Id, (uint)item.Market!.Ceiling, uint.MaxValue))
            .ToArray();

        IReadOnlyList<MarketBoard> parts = MarketBoard.CreateParts(goods);

        Assert.That(parts.Select(part => part.Entries.Count), Is.EqualTo(new[] { 13, 13, 10 }));
        var read = new List<MarketBoardEntry>();
        foreach (MarketBoard part in parts)
        {
            byte[] written = Encode(part.GetEncodedLength(), bytes => part.Write(bytes));
            Assert.That(MarketBoard.TryRead(written, out MarketBoard? back), Is.True);
            read.AddRange(back!.Entries);
        }

        Assert.That(
            read.Select(entry => (entry.Item, entry.Price, entry.Stock)),
            Is.EqualTo(goods.Select(entry => (entry.Item, entry.Price, entry.Stock))));
        Assert.That(read.Select(entry => entry.Item), Is.SupersetOf(QuarryCards), "the quarry's cards among them");
    }

    // The boss's casts, its fall to the longest name a character may have, its return, and its award.
    [Test]
    public void TheFurnaceHeartsCastsAnnouncementsAndAward_FitTheirMessages_AndReadBack()
    {
        string longestName = new('W', ProtocolLimits.MaxCharacterNameBytes);
        foreach (string skill in new[] { "skill.furnace_burst", "skill.ash_cloud" })
        {
            var started = new SkillCastStarted(Largest, new SkillDefinitionId(skill), Largest, uint.MaxValue, 2000);
            var resolved = new SkillResolved(
                Largest,
                Largest,
                new SkillDefinitionId(skill),
                SkillOutcome.Hit,
                uint.MaxValue,
                uint.MaxValue,
                1000);

            byte[] startedBytes = Encode(started.GetEncodedLength(), bytes => started.Write(bytes));
            byte[] resolvedBytes = Encode(resolved.GetEncodedLength(), bytes => resolved.Write(bytes));

            Assert.That(SkillCastStarted.TryRead(startedBytes, out SkillCastStarted? startedBack), Is.True, skill);
            Assert.That(startedBack!.Skill.Value, Is.EqualTo(skill));
            Assert.That(SkillResolved.TryRead(resolvedBytes, out SkillResolved? resolvedBack), Is.True, skill);
            Assert.That(resolvedBack!.Skill.Value, Is.EqualTo(skill));
        }

        foreach ((BossAnnouncementKind kind, string name) in new[]
                 {
                     (BossAnnouncementKind.Fell, longestName), (BossAnnouncementKind.Appeared, string.Empty)
                 })
        {
            var announcement = new BossAnnouncement(kind, FurnaceHeart, name);
            byte[] written = Encode(announcement.GetEncodedLength(), bytes => announcement.Write(bytes));
            Assert.That(BossAnnouncement.TryRead(written, out BossAnnouncement? back), Is.True, kind.ToString());
            Assert.That((back!.Kind, back.Monster, back.Name), Is.EqualTo((kind, FurnaceHeart, name)));
        }

        var award = new MvpAwarded(
            FurnaceHeart,
            ulong.MaxValue,
            new ItemDefinitionId("item.material.emberstone"),
            uint.MaxValue,
            PrizePlacement.Feet);
        byte[] awarded = Encode(award.GetEncodedLength(), bytes => award.Write(bytes));
        Assert.That(MvpAwarded.TryRead(awarded, out MvpAwarded? awardBack), Is.True);
        Assert.That(
            (awardBack!.Monster, awardBack.MvpExperience, awardBack.Item, awardBack.Amount, awardBack.Placed),
            Is.EqualTo((FurnaceHeart, ulong.MaxValue,
                (ItemDefinitionId?)new ItemDefinitionId("item.material.emberstone"),
                uint.MaxValue, PrizePlacement.Feet)));
    }

    // 14 bytes of header and five offers of 26 bytes each and their IDs, 90 and 108 bytes.
    [Test]
    public void TheGateWarden_OffersItsFiveQuests_InOneMessageOf342Bytes()
    {
        NpcServices services = NpcServicesBuilder.Build(
            Largest,
            Content.Npcs[new NpcDefinitionId("npc.gate_warden")],
            Content);

        byte[] written = Encode(services.GetEncodedLength(), bytes => services.Write(bytes));

        Assert.That(written, Has.Length.EqualTo(342));
        Assert.That(NpcServices.TryRead(written, out NpcServices? read), Is.True);
        Assert.That(
            read!.Offers.Select(offer => (offer.Quest.Value, offer.Monster.Value, offer.Count)),
            Is.EqualTo(services.Offers.Select(offer => (offer.Quest.Value, offer.Monster.Value, offer.Count))));
        Assert.That(
            read.Offers.Select(offer => offer.Quest.Value),
            Is.EquivalentTo(Content.Quests.Keys.Select(quest => quest.Value)),
            "every quest");
    }

    // Network Protocol §6: 13 bytes of header and 6 bytes and an ID for each card, 340 bytes of IDs in the first part
    // and 72 in the second.
    [Test]
    public void TheSmithsFees_OfTheSeventeenCards_TakeTwoPartsOf437And103Bytes()
    {
        IReadOnlyList<CardRemovalFees> parts = NpcServicesBuilder.BuildCardRemovalFees(
            Largest,
            Content.Npcs[new NpcDefinitionId("npc.smith")],
            Content);

        var read = new List<CardRemovalFee>();
        var lengths = new List<int>();
        foreach (CardRemovalFees part in parts)
        {
            byte[] written = Encode(part.GetEncodedLength(), bytes => part.Write(bytes));
            lengths.Add(written.Length);
            Assert.That(CardRemovalFees.TryRead(written, out CardRemovalFees? back), Is.True);
            Assert.That((back!.Npc, back.Part, back.PartCount), Is.EqualTo((part.Npc, part.Part, part.PartCount)));
            read.AddRange(back.Entries);
        }

        Assert.That(lengths, Is.EqualTo(new[] { 437, 103 }));
        Assert.That(
            read.Select(fee => fee.Card.Value),
            Is.EqualTo(Content.Items.Values.Where(item => item.Card != null)
                .Select(item => item.Id.Value)
                .OrderBy(card => card, StringComparer.Ordinal)),
            "every card once, by its ID");
        Assert.That(read.Where(fee => QuarryCards.Contains(fee.Card)).Select(fee => fee.Fee), Is.EqualTo(
            new[] { 600u, 600u, 600u, 5000u }));
    }
}
}
