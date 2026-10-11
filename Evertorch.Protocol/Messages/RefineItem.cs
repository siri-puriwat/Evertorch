using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Refines the bag's row <see cref="InventoryItem" /> one level at the Smith <see cref="Npc" />, from the level the
///     client shows for it (Gameplay Systems §11.6). Answered after the commit; the outcome is the server's draw.
/// </summary>
public sealed class RefineItem
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + sizeof(byte) + sizeof(uint);

    public RefineItem(EntityId npc, long inventoryItem, byte refineLevel, uint commandSequence)
    {
        if (npc.Value <= 0 || inventoryItem <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inventoryItem), "An NPC's and a row's IDs are at least 1.");
        }

        Npc = npc;
        InventoryItem = inventoryItem;
        RefineLevel = refineLevel;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public long InventoryItem { get; }

    /// <summary>
    ///     The level the client shows for the row; the server refuses a row at another.
    /// </summary>
    public byte RefineLevel { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including an NPC's or a row's ID below 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out RefineItem? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.RefineItem)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadInt64(out long row)
            || !reader.TryReadByte(out byte level)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || npc <= 0
            || row <= 0)
        {
            return false;
        }

        message = new RefineItem(new EntityId(npc), row, level, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.RefineItem);
        writer.WriteInt64(Npc.Value);
        writer.WriteInt64(InventoryItem);
        writer.WriteByte(RefineLevel);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
