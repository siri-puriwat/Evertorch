using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Invites the character named <see cref="Name" /> to the sender's party, or to a new one led by the sender
///     (Gameplay Systems §14). The invitee hears <see cref="PartyEvent" />; a refusal is
///     <see cref="CommandRejected" /> with this sequence.
/// </summary>
public sealed class PartyInvite
{
    public PartyInvite(string name, uint commandSequence)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        CommandSequence = commandSequence;
    }

    /// <summary>
    ///     The character invited.
    /// </summary>
    public string Name { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out PartyInvite? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyInvite)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || !CharacterNames.IsValid(name))
        {
            return false;
        }

        message = new PartyInvite(name, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes) + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyInvite);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
