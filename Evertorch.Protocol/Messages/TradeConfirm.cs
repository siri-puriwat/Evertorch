using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Confirms the trade, accepted only while both offers are locked (Gameplay Systems §16); once both
///     confirm, the server commits the exchange.
/// </summary>
public sealed class TradeConfirm
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint);

    public TradeConfirm(uint commandSequence)
    {
        CommandSequence = commandSequence;
    }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out TradeConfirm? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeConfirm)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new TradeConfirm(sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeConfirm);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
