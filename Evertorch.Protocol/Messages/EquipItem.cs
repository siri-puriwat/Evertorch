using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to wear an inventory row in <see cref="Destination" />, taking out what that position holds
///     (Gameplay Systems §11.1); an accessory goes to either accessory position, every other piece to its own. The
///     server checks everything and answers with <see cref="InventoryChanged" /> once the commit returns, or with
///     <see cref="CommandRejected" />.
/// </summary>
public readonly struct EquipItem
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(byte) + sizeof(uint);

    public EquipItem(long inventoryItem, EquipmentSlot destination, uint commandSequence)
    {
        InventoryItem = inventoryItem;
        Destination = destination;
        CommandSequence = commandSequence;
    }

    public long InventoryItem { get; }

    /// <summary>The position the row goes to, one of the eight.</summary>
    public EquipmentSlot Destination { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a row ID the database never gives, 0 or below, and a destination that
    ///     is no position.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out EquipItem message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EquipItem)
            || !reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadByte(out byte destination)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || inventoryItem <= 0
            || !EquipmentPositions.IsPosition((EquipmentSlot)destination))
        {
            return false;
        }

        message = new EquipItem(inventoryItem, (EquipmentSlot)destination, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EquipItem);
        writer.WriteInt64(InventoryItem);
        writer.WriteByte((byte)Destination);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
