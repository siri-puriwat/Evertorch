using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One movement intent on the wire. It asks for a direction and nothing more; the server decides where the entity
///     ends up. It names the map epoch of the world it was made for, and input for another is dropped.
/// </summary>
public readonly struct MoveInput
{
    public const int EncodedLength =
        sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(float) + sizeof(float) + sizeof(byte);

    public MoveInput(MoveIntent intent, byte mapEpoch = 0)
    {
        Intent = intent;
        MapEpoch = mapEpoch;
    }

    public MoveIntent Intent { get; }

    public byte MapEpoch { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out MoveInput message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MoveInput)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.TryReadUInt32(out uint clientTick)
            || !reader.TryReadSingle(out float directionX)
            || !reader.TryReadSingle(out float directionZ)
            || !reader.TryReadByte(out byte mapEpoch)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new MoveInput(new MoveIntent(sequence, clientTick, directionX, directionZ), mapEpoch);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MoveInput);
        writer.WriteUInt32(Intent.Sequence);
        writer.WriteUInt32(Intent.ClientTick);
        writer.WriteSingle(Intent.DirectionX);
        writer.WriteSingle(Intent.DirectionZ);
        writer.WriteByte(MapEpoch);
        return writer.Position;
    }
}
}
