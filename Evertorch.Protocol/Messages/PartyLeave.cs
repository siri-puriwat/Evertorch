using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Leaves the party (Gameplay Systems §14): a leader passes the lead to the earliest joined, and a party left
///     with one member disbands. Answered after the commit.
/// </summary>
public sealed class PartyLeave
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint);

    public PartyLeave(uint commandSequence)
    {
        CommandSequence = commandSequence;
    }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out PartyLeave? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyLeave)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new PartyLeave(sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyLeave);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
