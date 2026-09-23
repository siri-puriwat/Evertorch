using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to revive the sender's dead character at its map's spawn point. Refused while alive.
/// </summary>
public readonly struct Respawn
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint);

    public Respawn(uint commandSequence)
    {
        CommandSequence = commandSequence;
    }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out Respawn message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.Respawn)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new Respawn(sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.Respawn);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
