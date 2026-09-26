using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to use one unit of a consumable row (Gameplay Systems §11.2). The server checks it and answers
///     with <see cref="InventoryChanged" /> and the owner's health once the commit returns, or with
///     <see cref="CommandRejected" />.
/// </summary>
public readonly struct UseItem
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public UseItem(long inventoryItem, uint commandSequence)
    {
        InventoryItem = inventoryItem;
        CommandSequence = commandSequence;
    }

    public long InventoryItem { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a row ID the database never gives: 0 or below.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out UseItem message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.UseItem)
            || !reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || inventoryItem <= 0)
        {
            return false;
        }

        message = new UseItem(inventoryItem, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.UseItem);
        writer.WriteInt64(InventoryItem);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
