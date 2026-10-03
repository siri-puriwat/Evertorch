using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The answer to the request from the character named <see cref="Requester" /> (Gameplay Systems §16): an accept
///     opens the trade for both, a decline tells the requester.
/// </summary>
public sealed class TradeReply
{
    public TradeReply(string requester, bool isAccepted, uint commandSequence)
    {
        Requester = requester ?? throw new ArgumentNullException(nameof(requester));
        IsAccepted = isAccepted;
        CommandSequence = commandSequence;
    }

    public string Requester { get; }

    public bool IsAccepted { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule and an answer other than 0 or 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out TradeReply? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeReply)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string requester)
            || !reader.TryReadByte(out byte accept)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || accept > 1
            || !CharacterNames.IsValid(requester))
        {
            return false;
        }

        message = new TradeReply(requester, accept == 1, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Requester, ProtocolLimits.MaxCharacterNameBytes)
            + sizeof(byte)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeReply);
        writer.WriteString(Requester, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteByte(IsAccepted ? (byte)1 : (byte)0);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
