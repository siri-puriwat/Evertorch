using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to wear an inventory row in the slot its item fills, swapping out what the slot holds
///     (Gameplay Systems §11.1). The server checks everything and answers with <see cref="InventoryChanged" /> once
///     the commit returns, or with <see cref="CommandRejected" />.
/// </summary>
public readonly struct EquipItem
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public EquipItem(long inventoryItem, uint commandSequence)
    {
        InventoryItem = inventoryItem;
        CommandSequence = commandSequence;
    }

    public long InventoryItem { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a row ID the database never gives: 0 or below.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out EquipItem message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EquipItem)
            || !reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || inventoryItem <= 0)
        {
            return false;
        }

        message = new EquipItem(inventoryItem, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EquipItem);
        writer.WriteInt64(InventoryItem);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
