using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     What one refine did to the bag's row <see cref="InventoryItem" /> (Network Protocol §6, §9): sent to the refiner
///     alone, after the commit's inventory change, naming the item, its level before and after, and the outcome.
/// </summary>
public sealed class RefineOutcome
{
    public const int MaxEncodedLength =
        sizeof(ushort) + sizeof(long) + sizeof(ushort) + ProtocolLimits.MaxDefinitionIdBytes + 3 * sizeof(byte);

    public RefineOutcome(
        long inventoryItem,
        ItemDefinitionId item,
        byte levelBefore,
        byte levelAfter,
        RefineOutcomeKind outcome)
    {
        if (inventoryItem <= 0 || item.Value.Length == 0 || !IsValid(outcome, levelBefore, levelAfter))
        {
            throw new ArgumentException(
                "An outcome names its row and item: a refine one level up, a destroyed row at 0, a lowered one below "
                + "its level, an unchanged one at it.");
        }

        InventoryItem = inventoryItem;
        Item = item;
        LevelBefore = levelBefore;
        LevelAfter = levelAfter;
        Outcome = outcome;
    }

    public long InventoryItem { get; }

    public ItemDefinitionId Item { get; }

    public byte LevelBefore { get; }

    /// <summary>
    ///     0 for a destroyed row.
    /// </summary>
    public byte LevelAfter { get; }

    public RefineOutcomeKind Outcome { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out RefineOutcome? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.RefineOutcome)
            || !reader.TryReadInt64(out long row)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadByte(out byte before)
            || !reader.TryReadByte(out byte after)
            || !reader.TryReadByte(out byte outcome)
            || !reader.IsAtEnd
            || row <= 0
            || !IsValid((RefineOutcomeKind)outcome, before, after)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        message = new RefineOutcome(row, item, before, after, (RefineOutcomeKind)outcome);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(long)
            + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + 3 * sizeof(byte);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.RefineOutcome);
        writer.WriteInt64(InventoryItem);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteByte(LevelBefore);
        writer.WriteByte(LevelAfter);
        writer.WriteByte((byte)Outcome);
        return writer.Position;
    }

    private static bool IsValid(RefineOutcomeKind outcome, byte before, byte after)
    {
        return outcome switch
        {
            RefineOutcomeKind.Refined => after == before + 1,
            RefineOutcomeKind.Destroyed => after == 0,
            RefineOutcomeKind.Lowered => after < before,
            RefineOutcomeKind.Unchanged => after == before,
            _ => false
        };
    }
}
}
