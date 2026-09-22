using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to stop the entity. It shares the input sequence space with <see cref="MoveInput" />, so a stop
///     that arrives after a newer move is recognised as stale.
/// </summary>
public readonly struct StopMovement
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint) + sizeof(uint);

    public StopMovement(uint sequence, uint clientTick)
    {
        Sequence = sequence;
        ClientTick = clientTick;
    }

    public uint Sequence { get; }

    public uint ClientTick { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out StopMovement message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StopMovement)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.TryReadUInt32(out uint clientTick)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new StopMovement(sequence, clientTick);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StopMovement);
        writer.WriteUInt32(Sequence);
        writer.WriteUInt32(ClientTick);
        return writer.Position;
    }
}
}
