using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
/// One movement intent on the wire. It asks for a direction and nothing more; the server decides where the entity
/// ends up.
/// </summary>
public readonly struct MoveInput
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(float) + sizeof(float);

    public MoveInput(MoveIntent intent)
    {
        Intent = intent;
    }

    public MoveIntent Intent { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out MoveInput message)
    {
        message = default;
        WireReader reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MoveInput)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.TryReadUInt32(out uint clientTick)
            || !reader.TryReadSingle(out float directionX)
            || !reader.TryReadSingle(out float directionZ)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new MoveInput(new MoveIntent(sequence, clientTick, directionX, directionZ));
        return true;
    }

    public int Write(Span<byte> destination)
    {
        WireWriter writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MoveInput);
        writer.WriteUInt32(Intent.Sequence);
        writer.WriteUInt32(Intent.ClientTick);
        writer.WriteSingle(Intent.DirectionX);
        writer.WriteSingle(Intent.DirectionZ);
        return writer.Position;
    }
}
}
