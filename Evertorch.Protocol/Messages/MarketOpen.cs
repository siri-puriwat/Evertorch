using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Reads the market board at the Broker <see cref="Npc" /> (Gameplay Systems §11.5), answered with the board's
///     entries and then the character's listings.
/// </summary>
public sealed class MarketOpen
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public MarketOpen(EntityId npc, uint commandSequence)
    {
        Npc = npc;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out MarketOpen? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MarketOpen)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new MarketOpen(new EntityId(npc), sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MarketOpen);
        writer.WriteInt64(Npc.Value);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
