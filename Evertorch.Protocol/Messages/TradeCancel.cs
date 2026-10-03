using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Ends the player's open trade for both traders, or withdraws its own request, until the commit starts
///     (Gameplay Systems §16).
/// </summary>
public sealed class TradeCancel
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint);

    public TradeCancel(uint commandSequence)
    {
        CommandSequence = commandSequence;
    }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out TradeCancel? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeCancel)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new TradeCancel(sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeCancel);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
