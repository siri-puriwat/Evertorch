using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedRefineMessageTests
{
    private static readonly byte[] RefineItemBytes =
    {
        0x2D, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x04, 0x00, 0x00, 0x00
    };

    private static readonly byte[] RefineTableBytes =
    {
        0x2D, 0x80, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E,
        0x62, 0x01, 0x01, 0x00, 0x02, 0x64, 0x14, 0x00, 0x00, 0x00, 0x3C, 0x14, 0x00, 0x00, 0x00, 0x01, 0x06, 0x00,
        0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61
    };

    private static readonly byte[] RefineOutcomeBytes =
    {
        0x2E, 0x80, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x06, 0x07, 0x01
    };

    private static readonly ItemDefinitionId ItemA = new("item.a");
    private static readonly ItemDefinitionId ItemB = new("item.b");

    [Test]
    public void TheCommand_WritesItsGoldenBytes_AndReadsThemBack()
    {
        var command = new RefineItem(new EntityId(7), 42, 6, 4);
        byte[] written = new byte[RefineItem.EncodedLength];
        command.Write(written);

        Assert.That(written, Is.EqualTo(RefineItemBytes));
        Assert.That(RefineItem.TryRead(RefineItemBytes, out RefineItem? read), Is.True);
        Assert.That((read!.InventoryItem, read.RefineLevel, read.CommandSequence), Is.EqualTo((42L, (byte)6, 4u)));
    }

    [Test]
    public void TheTableAndTheOutcome_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var table = new RefineTable(
            new EntityId(7),
            0,
            ItemB,
            1,
            RefineFailure.Destroy,
            0,
            new[] { new RefineTableStep(100, 20), new RefineTableStep(60, 20) },
            new[] { ItemA });
        var outcome = new RefineOutcome(42, ItemA, 6, 7, RefineOutcomeKind.Refined);
        byte[] tableWritten = new byte[table.GetEncodedLength()];
        table.Write(tableWritten);
        byte[] outcomeWritten = new byte[outcome.GetEncodedLength()];
        outcome.Write(outcomeWritten);

        Assert.That(tableWritten, Is.EqualTo(RefineTableBytes));
        Assert.That(outcomeWritten, Is.EqualTo(RefineOutcomeBytes));
        Assert.That(RefineTable.TryRead(RefineTableBytes, out RefineTable? readTable), Is.True);
        Assert.That(
            readTable!.Steps.Select(step => (int)step.Chance).ToArray(),
            Is.EqualTo(new[] { 100, 60 }));
        Assert.That(readTable.Items.Single(), Is.EqualTo(ItemA));
        Assert.That(RefineOutcome.TryRead(RefineOutcomeBytes, out RefineOutcome? readOutcome), Is.True);
        Assert.That(readOutcome!.Outcome, Is.EqualTo(RefineOutcomeKind.Refined));
        Assert.That(readOutcome.LevelAfter, Is.EqualTo((byte)7));
    }
}
}
