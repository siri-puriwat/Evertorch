using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Something that happened to the player's trade or its request (Network Protocol §9): the kind, the name it is
///     about, and, for a failure or an unsaved trade, why. The client puts it into words.
/// </summary>
public sealed class TradeEvent
{
    public TradeEvent(TradeEventKind kind, string name, CommandRejectionReason reason = CommandRejectionReason.None)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        if (HasReason(kind) != (reason != CommandRejectionReason.None))
        {
            throw new ArgumentException("A failure and an unsaved trade carry a reason; nothing else does.",
                nameof(reason));
        }

        Kind = kind;
        Reason = reason;
    }

    public TradeEventKind Kind { get; }

    public string Name { get; }

    public CommandRejectionReason Reason { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule, and a reason missing from a
    ///     failure or an unsaved trade or given to any other kind.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out TradeEvent? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeEvent)
            || !reader.TryReadByte(out byte kindValue)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.TryReadByte(out byte reasonValue)
            || !reader.IsAtEnd)
        {
            return false;
        }

        var kind = (TradeEventKind)kindValue;
        var reason = (CommandRejectionReason)reasonValue;
        if (!WireEnums.IsDefined(kind)
            || !CharacterNames.IsValid(name)
            || (reason != CommandRejectionReason.None && !WireEnums.IsDefined(reason))
            || HasReason(kind) != (reason != CommandRejectionReason.None))
        {
            return false;
        }

        message = new TradeEvent(kind, name, reason);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(byte)
            + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes)
            + sizeof(byte);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeEvent);
        writer.WriteByte((byte)Kind);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteByte((byte)Reason);
        return writer.Position;
    }

    private static bool HasReason(TradeEventKind kind)
    {
        return kind == TradeEventKind.Failed || kind == TradeEventKind.Unsaved;
    }
}
}
