using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Ends the sender's auto-attack. It names no action; a swing that has begun still resolves.
/// </summary>
public readonly struct CancelAction
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint);

    public CancelAction(uint commandSequence)
    {
        CommandSequence = commandSequence;
    }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CancelAction message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CancelAction)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new CancelAction(sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CancelAction);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
