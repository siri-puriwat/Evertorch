using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The leader passes the lead to the member named <see cref="Member" />, online or not (Gameplay Systems
///     §14).
/// </summary>
public sealed class PartyLead
{
    public PartyLead(string member, uint commandSequence)
    {
        Member = member ?? throw new ArgumentNullException(nameof(member));
        CommandSequence = commandSequence;
    }

    /// <summary>
    ///     The new leader.
    /// </summary>
    public string Member { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out PartyLead? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyLead)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string member)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || !CharacterNames.IsValid(member))
        {
            return false;
        }

        message = new PartyLead(member, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + WireText.GetEncodedLength(Member, ProtocolLimits.MaxCharacterNameBytes) + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyLead);
        writer.WriteString(Member, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
