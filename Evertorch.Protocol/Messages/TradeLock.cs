using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Freezes the player's own offer in its open trade (Gameplay Systems §16); there is no unlock.
/// </summary>
public sealed class TradeLock
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint);

    public TradeLock(uint commandSequence)
    {
        CommandSequence = commandSequence;
    }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out TradeLock? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeLock)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new TradeLock(sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeLock);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
