using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     An inspection's parts put together (Network Protocol §9): one answer awaited, kept whole or not at all.
/// </summary>
[TestFixture]
public sealed class InspectionPartsTests
{
    private const uint Sequence = 7;
    private const uint Revision = 3;

    private static readonly EntityId Target = new(9);

    private static readonly ItemDefinitionId[] Longest = Enumerable.Range(0, 5)
        .Select(index =>
            new ItemDefinitionId($"item.{(char)('a' + index)}{new string('c', DefinitionIdLimits.MaxLength - 6)}"))
        .ToArray();

    // The wire's worst case: every position holds the longest item with four of the longest cards, three parts.
    private static IReadOnlyList<InspectedEquipment> WorstCase(uint sequence = Sequence, uint revision = Revision)
    {
        InspectedEntry[] worn = EquipmentPositions.All
            .Select(slot => new InspectedEntry(slot, new InspectedItem(Longest[4], 10, Longest.Take(4).ToArray())))
            .ToArray();
        return InspectedEquipment.CreateParts(Target, sequence, revision, worn);
    }

    private static InspectedEquipment Bare(uint sequence = Sequence)
    {
        return InspectedEquipment.CreateParts(
                Target,
                sequence,
                Revision,
                EquipmentPositions.All.Select(slot => new InspectedEntry(slot, default)).ToArray())
            .Single();
    }

    private static InspectionParts Waiting()
    {
        var parts = new InspectionParts();
        parts.Expect(Sequence, Target, 0);
        return parts;
    }

    [Test]
    public void Answer_InOnePart_IsWholeAtOnce()
    {
        InspectionParts parts = Waiting();

        bool isWhole = parts.TryAdd(Bare(), out PlayerInspection? whole);

        Assert.That(isWhole, Is.True);
        Assert.That((whole!.Entity, whole.Revision), Is.EqualTo((Target, Revision)));
        Assert.That(whole.Items.All(item => item.IsEmpty), Is.True);
        Assert.That((parts.IsWaiting, parts.Dropped), Is.EqualTo((false, 0)));
    }

    [Test]
    public void Answer_InThreeParts_IsWholeAtTheLast_InAnyOrder()
    {
        InspectionParts parts = Waiting();
        IReadOnlyList<InspectedEquipment> answer = WorstCase();

        bool first = parts.TryAdd(answer[2], out _);
        bool second = parts.TryAdd(answer[0], out _);
        bool last = parts.TryAdd(answer[1], out PlayerInspection? whole);

        Assert.That((answer.Count, first, second, last), Is.EqualTo((3, false, false, true)));
        Assert.That(whole!.ItemIn(EquipmentSlot.Accessory2).Cards, Is.EqualTo(Longest.Take(4)));
        Assert.That(whole.Items.Select(item => item.RefineLevel), Is.All.EqualTo((byte)10));
    }

    [Test]
    public void Answer_LateOrAfterATimeoutOrAWorldChange_IsDropped()
    {
        var neverAsked = new InspectionParts();
        InspectionParts timedOut = Waiting();
        timedOut.Advance(InspectionParts.TimeoutSeconds + 0.1);
        InspectionParts forgotten = Waiting();
        forgotten.Forget();
        InspectionParts answered = Waiting();
        answered.TryAdd(Bare(), out _);

        Assert.That(neverAsked.TryAdd(Bare(), out _), Is.False);
        Assert.That(timedOut.TryAdd(Bare(), out _), Is.False);
        Assert.That(forgotten.TryAdd(Bare(), out _), Is.False);
        Assert.That(answered.TryAdd(Bare(), out _), Is.False, "a duplicate after the whole answer");
        Assert.That(
            new[] { neverAsked.Dropped, timedOut.Dropped, forgotten.Dropped, answered.Dropped },
            Is.All.GreaterThan(0));
    }

    [Test]
    public void Answer_ThatIsMixedDuplicatedOrIncomplete_IsKeptNotAtAll()
    {
        IReadOnlyList<InspectedEquipment> answer = WorstCase();
        IReadOnlyList<InspectedEquipment> otherRevision = WorstCase(revision: Revision + 1);
        var cases = new Dictionary<string, InspectedEquipment[]>
        {
            ["another revision"] = new[] { answer[0], otherRevision[1], answer[2] },
            ["a part twice"] = new[] { answer[0], answer[0], answer[1], answer[2] },
            ["another request"] = new[] { answer[0], WorstCase(Sequence + 1)[1], answer[2] },
            ["a position twice"] = new[]
            {
                answer[0], answer[1],
                new InspectedEquipment(Target, Sequence, Revision, 2, 3, answer[0].Entries.Take(2).ToArray())
            },
            ["a position missing"] = new[]
            {
                answer[0], answer[1],
                new InspectedEquipment(Target, Sequence, Revision, 2, 3, answer[2].Entries.Take(1).ToArray())
            }
        };

        foreach (KeyValuePair<string, InspectedEquipment[]> pair in cases)
        {
            InspectionParts parts = Waiting();
            PlayerInspection? whole = null;
            foreach (InspectedEquipment part in pair.Value)
            {
                if (parts.TryAdd(part, out PlayerInspection? done))
                {
                    whole = done;
                }
            }

            Assert.That(whole, Is.Null, pair.Key);
            Assert.That(parts.Dropped, Is.GreaterThan(0), pair.Key);
        }
    }

    [Test]
    public void SecondRequest_ReplacesTheFirst_WhoseAnswerIsThenDropped()
    {
        InspectionParts parts = Waiting();
        parts.Expect(Sequence + 1, Target, 1);

        bool isOld = parts.TryAdd(Bare(), out _);
        bool isNew = parts.TryAdd(Bare(Sequence + 1), out PlayerInspection? whole);

        Assert.That((isOld, isNew, whole != null), Is.EqualTo((false, true, true)));
    }
}
}
