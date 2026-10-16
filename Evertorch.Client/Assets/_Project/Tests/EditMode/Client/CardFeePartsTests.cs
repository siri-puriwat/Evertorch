using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     A Smith's removal fees put together from their parts (Network Protocol §9): one list at a time, in order, kept
///     whole or not at all.
/// </summary>
[TestFixture]
public sealed class CardFeePartsTests
{
    private static readonly EntityId Smith = new(14);
    private static readonly EntityId OtherSmith = new(15);

    // Seventeen cards, the content of Milestone 21: fourteen in the first part and three in the second.
    private static IReadOnlyList<CardRemovalFees> Seventeen(EntityId npc)
    {
        CardRemovalFee[] fees = Enumerable.Range(0, 17)
            .Select(index => new CardRemovalFee(new ItemDefinitionId($"item.card.c{index:D2}"), (uint)(100 + index)))
            .ToArray();
        return CardRemovalFees.CreateParts(npc, fees);
    }

    [Test]
    public void ACardListedInTwoParts_DropsTheList()
    {
        var parts = new CardFeeParts();
        IReadOnlyList<CardRemovalFees> seventeen = Seventeen(Smith);
        CardRemovalFees repeating = new(
            Smith,
            1,
            2,
            new[] { new CardRemovalFee(seventeen[0].Entries[3].Card, 999) });
        parts.TryAdd(seventeen[0], 0, out _);

        bool isWhole = parts.TryAdd(repeating, 0, out _);

        Assert.That((isWhole, parts.IsAssembling, parts.Dropped), Is.EqualTo((false, false, 1)));
    }

    [Test]
    public void AListWaitingPastItsTimeout_IsDropped_AndItsLatePartWithIt()
    {
        var parts = new CardFeeParts();
        IReadOnlyList<CardRemovalFees> seventeen = Seventeen(Smith);
        parts.TryAdd(seventeen[0], 10, out _);

        parts.Advance(10 + CardFeeParts.TimeoutSeconds);
        bool isWaitingAtTheLimit = parts.IsAssembling;
        parts.Advance(10.1 + CardFeeParts.TimeoutSeconds);
        bool isWhole = parts.TryAdd(seventeen[1], 16, out _);

        Assert.That(isWaitingAtTheLimit, Is.True, "at the limit itself");
        Assert.That((isWhole, parts.IsAssembling, parts.Dropped), Is.EqualTo((false, false, 2)));
    }

    [Test]
    public void APartBeforeAnyPartZero_IsDroppedAlone()
    {
        var parts = new CardFeeParts();
        IReadOnlyList<CardRemovalFees> seventeen = Seventeen(Smith);

        bool isWhole = parts.TryAdd(seventeen[1], 0, out _);

        Assert.That((isWhole, parts.IsAssembling, parts.Dropped), Is.EqualTo((false, false, 1)));
    }

    [Test]
    public void APartOfAnotherSmithOrCount_DropsTheList()
    {
        var parts = new CardFeeParts();
        parts.TryAdd(Seventeen(Smith)[0], 0, out _);
        bool isWholeFromAnother = parts.TryAdd(Seventeen(OtherSmith)[1], 0, out _);

        parts.TryAdd(Seventeen(Smith)[0], 0, out _);
        CardRemovalFees ofThree = new(
            Smith,
            1,
            3,
            new[] { new CardRemovalFee(new ItemDefinitionId("item.card.z"), 100) });
        bool isWholeAtAnotherCount = parts.TryAdd(ofThree, 0, out _);

        Assert.That((isWholeFromAnother, isWholeAtAnotherCount), Is.EqualTo((false, false)));
        Assert.That((parts.IsAssembling, parts.Dropped), Is.EqualTo((false, 2)));
    }

    // Thirty cards take three parts; the third after the first drops the list, and the second, late, is a stray.
    [Test]
    public void APartOutOfOrder_DropsTheList()
    {
        var parts = new CardFeeParts();
        IReadOnlyList<CardRemovalFees> thirty = CardRemovalFees.CreateParts(
            Smith,
            Enumerable.Range(0, 30)
                .Select(index => new CardRemovalFee(new ItemDefinitionId($"item.card.c{index:D2}"), 100))
                .ToArray());
        parts.TryAdd(thirty[0], 0, out _);

        bool isWholeAtTheThird = parts.TryAdd(thirty[2], 0, out _);
        bool isAssemblingAfter = parts.IsAssembling;
        bool isWholeAtTheSecond = parts.TryAdd(thirty[1], 0, out _);

        Assert.That(thirty.Select(part => part.Entries.Count), Is.EqualTo(new[] { 14, 14, 2 }));
        Assert.That((isWholeAtTheThird, isAssemblingAfter, isWholeAtTheSecond), Is.EqualTo((false, false, false)));
        Assert.That(parts.Dropped, Is.EqualTo(2));
    }

    [Test]
    public void APartRepeated_DropsTheList()
    {
        var parts = new CardFeeParts();
        IReadOnlyList<CardRemovalFees> seventeen = Seventeen(Smith);
        parts.TryAdd(seventeen[0], 0, out _);

        bool isWhole = parts.TryAdd(seventeen[0], 0, out _);
        bool isWholeAfter = parts.TryAdd(seventeen[1], 0, out _);

        // The repeated part 0 drops the first list and begins another, which the second part then completes.
        Assert.That((isWhole, isWholeAfter, parts.Dropped), Is.EqualTo((false, true, 1)));
    }

    [Test]
    public void List_InOnePart_IsWholeAtOnce()
    {
        var parts = new CardFeeParts();
        CardRemovalFees single = CardRemovalFees.CreateParts(
                Smith,
                new[] { new CardRemovalFee(new ItemDefinitionId("item.card.a"), 200) })
            .Single();

        bool isWhole = parts.TryAdd(single, 0, out IReadOnlyList<CardRemovalFee> fees);

        Assert.That(isWhole, Is.True);
        Assert.That(fees.Select(fee => (fee.Card.Value, fee.Fee)), Is.EqualTo(new[] { ("item.card.a", 200u) }));
        Assert.That((parts.IsAssembling, parts.Dropped), Is.EqualTo((false, 0)));
    }

    [Test]
    public void List_InTwoParts_IsWholeOnlyAtTheSecond()
    {
        var parts = new CardFeeParts();
        IReadOnlyList<CardRemovalFees> seventeen = Seventeen(Smith);

        bool isWholeAtFirst = parts.TryAdd(seventeen[0], 0, out IReadOnlyList<CardRemovalFee> partial);
        bool isWholeAtSecond = parts.TryAdd(seventeen[1], 0.05, out IReadOnlyList<CardRemovalFee> whole);

        Assert.That(seventeen.Select(part => part.Entries.Count), Is.EqualTo(new[] { 14, 3 }));
        Assert.That((isWholeAtFirst, partial.Count), Is.EqualTo((false, 0)));
        Assert.That(isWholeAtSecond, Is.True);
        Assert.That(
            whole.Select(fee => fee.Card.Value),
            Is.EqualTo(Enumerable.Range(0, 17).Select(index => $"item.card.c{index:D2}")));
        Assert.That(parts.Dropped, Is.Zero);
    }

    [Test]
    public void TheSmithsDespawn_DropsItsList_AndLeavesAnotherAlone()
    {
        var parts = new CardFeeParts();
        parts.TryAdd(Seventeen(Smith)[0], 0, out _);

        parts.Forget(OtherSmith);
        bool isWaitingAfterAnother = parts.IsAssembling;
        parts.Forget(Smith);

        Assert.That(isWaitingAfterAnother, Is.True);
        Assert.That((parts.IsAssembling, parts.Dropped), Is.EqualTo((false, 1)));
    }
}
}
