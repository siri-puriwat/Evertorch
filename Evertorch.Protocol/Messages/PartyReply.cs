using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The answer to the invite from the character named <see cref="Inviter" /> (Gameplay Systems §14): an accept
///     joins its party, or makes one, and is answered after the commit; a decline tells the inviter.
/// </summary>
public sealed class PartyReply
{
    public PartyReply(string inviter, bool isAccepted, uint commandSequence)
    {
        Inviter = inviter ?? throw new ArgumentNullException(nameof(inviter));
        IsAccepted = isAccepted;
        CommandSequence = commandSequence;
    }

    public string Inviter { get; }

    public bool IsAccepted { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule and an answer other than 0 or 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out PartyReply? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyReply)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string inviter)
            || !reader.TryReadByte(out byte accept)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || accept > 1
            || !CharacterNames.IsValid(inviter))
        {
            return false;
        }

        message = new PartyReply(inviter, accept == 1, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Inviter, ProtocolLimits.MaxCharacterNameBytes)
            + sizeof(byte)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyReply);
        writer.WriteString(Inviter, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteByte(IsAccepted ? (byte)1 : (byte)0);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
