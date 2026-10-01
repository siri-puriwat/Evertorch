using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Something that happened to the party, or an invite for the player (Network Protocol §9): the kind and the
///     name it is about, which the client puts into words.
/// </summary>
public sealed class PartyEvent
{
    public PartyEvent(PartyEventKind kind, string name)
    {
        Kind = kind;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public PartyEventKind Kind { get; }

    public string Name { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out PartyEvent? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyEvent)
            || !reader.TryReadByte(out byte kindValue)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.IsAtEnd)
        {
            return false;
        }

        var kind = (PartyEventKind)kindValue;
        if (!WireEnums.IsDefined(kind) || !CharacterNames.IsValid(name))
        {
            return false;
        }

        message = new PartyEvent(kind, name);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + sizeof(byte) + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyEvent);
        writer.WriteByte((byte)Kind);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        return writer.Position;
    }
}
}
