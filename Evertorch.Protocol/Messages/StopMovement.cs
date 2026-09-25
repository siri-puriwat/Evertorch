using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to stop the entity. It shares the input sequence space with <see cref="MoveInput" />, so a stop
///     that arrives after a newer move is recognised as stale, and names the map epoch as it does.
/// </summary>
public readonly struct StopMovement
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(byte);

    public StopMovement(uint sequence, uint clientTick, byte mapEpoch = 0)
    {
        Sequence = sequence;
        ClientTick = clientTick;
        MapEpoch = mapEpoch;
    }

    public uint Sequence { get; }

    public uint ClientTick { get; }

    public byte MapEpoch { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out StopMovement message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StopMovement)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.TryReadUInt32(out uint clientTick)
            || !reader.TryReadByte(out byte mapEpoch)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new StopMovement(sequence, clientTick, mapEpoch);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StopMovement);
        writer.WriteUInt32(Sequence);
        writer.WriteUInt32(ClientTick);
        writer.WriteByte(MapEpoch);
        return writer.Position;
    }
}
}
