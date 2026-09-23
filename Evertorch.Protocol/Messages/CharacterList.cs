using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The signed-in account's characters (Network Protocol §4): sent after <see cref="ServerHello" />, after every
///     <see cref="CreateCharacterResult" />, and after a logout.
/// </summary>
public sealed class CharacterList
{
    /// <summary>
    ///     The most characters one account may hold, so the whole list always fits one datagram.
    /// </summary>
    public const int MaxEntries = 3;

    public CharacterList(IReadOnlyList<CharacterListEntry> characters)
    {
        Characters = characters ?? throw new ArgumentNullException(nameof(characters));
        if (characters.Count > MaxEntries)
        {
            throw new ArgumentException($"A character list holds at most {MaxEntries} entries.", nameof(characters));
        }
    }

    public IReadOnlyList<CharacterListEntry> Characters { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CharacterList? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CharacterList)
            || !reader.TryReadByte(out byte count)
            || count > MaxEntries)
        {
            return false;
        }

        var characters = new CharacterListEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadInt64(out long character)
                || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
                || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string jobText)
                || !reader.TryReadUInt16(out ushort baseLevel)
                || character <= 0
                || baseLevel == 0
                || !JobDefinitionId.TryCreate(jobText, out JobDefinitionId job))
            {
                return false;
            }

            characters[index] = new CharacterListEntry(new CharacterId(character), name, job, baseLevel);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new CharacterList(characters);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(byte);
        foreach (CharacterListEntry entry in Characters)
        {
            length += sizeof(long)
                + WireText.GetEncodedLength(entry.Name, ProtocolLimits.MaxCharacterNameBytes)
                + WireText.GetEncodedLength(entry.Job.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + sizeof(ushort);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CharacterList);
        writer.WriteByte((byte)Characters.Count);
        foreach (CharacterListEntry entry in Characters)
        {
            writer.WriteInt64(entry.Character.Value);
            writer.WriteString(entry.Name, ProtocolLimits.MaxCharacterNameBytes);
            writer.WriteString(entry.Job.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt16(entry.BaseLevel);
        }

        return writer.Position;
    }
}
}
