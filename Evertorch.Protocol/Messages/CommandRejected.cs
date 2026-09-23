using System;

namespace Evertorch.Protocol
{
/// <summary>
///     The answer to a refused command that carried a command sequence (Network Protocol §11). A command whose
///     sequence was stale gets no answer.
/// </summary>
public readonly struct CommandRejected
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint) + sizeof(byte);

    public CommandRejected(uint commandSequence, CommandRejectionReason reason)
    {
        CommandSequence = commandSequence;
        Reason = reason;
    }

    public uint CommandSequence { get; }

    public CommandRejectionReason Reason { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CommandRejected message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CommandRejected)
            || !reader.TryReadUInt32(out uint commandSequence)
            || !reader.TryReadByte(out byte reason)
            || !reader.IsAtEnd
            || !WireEnums.IsDefined((CommandRejectionReason)reason))
        {
            return false;
        }

        message = new CommandRejected(commandSequence, (CommandRejectionReason)reason);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CommandRejected);
        writer.WriteUInt32(CommandSequence);
        writer.WriteByte((byte)Reason);
        return writer.Position;
    }
}
}
