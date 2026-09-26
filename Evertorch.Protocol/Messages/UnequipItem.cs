using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to empty an equipment slot (Gameplay Systems §11.1); the item stays in the inventory.
/// </summary>
public readonly struct UnequipItem
{
    public const int EncodedLength = sizeof(ushort) + sizeof(byte) + sizeof(uint);

    public UnequipItem(EquipmentSlot slot, uint commandSequence)
    {
        Slot = slot;
        CommandSequence = commandSequence;
    }

    public EquipmentSlot Slot { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a slot other than the weapon's or the armor's.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out UnequipItem message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.UnequipItem)
            || !reader.TryReadByte(out byte slot)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || (slot != (byte)EquipmentSlot.Weapon && slot != (byte)EquipmentSlot.Armor))
        {
            return false;
        }

        message = new UnequipItem((EquipmentSlot)slot, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.UnequipItem);
        writer.WriteByte((byte)Slot);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
