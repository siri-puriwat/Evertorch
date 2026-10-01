using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The player's party (Network Protocol §9): its members in the order they joined and which one leads, offline
///     members included; no members when the player has no party. Sent in every full baseline and again whenever the
///     membership changes or a member enters or leaves the world or changes its map, base level, or job.
/// </summary>
public sealed class PartyRoster
{
    /// <summary>
    ///     The most members a party holds (Gameplay Systems §14), so the roster always fits one message.
    /// </summary>
    public const int MaxMembers = 5;

    public PartyRoster(byte leaderIndex, IReadOnlyList<PartyRosterEntry> members)
    {
        Members = members ?? throw new ArgumentNullException(nameof(members));
        if (members.Count > MaxMembers)
        {
            throw new ArgumentException($"A party holds at most {MaxMembers} members.", nameof(members));
        }

        LeaderIndex = leaderIndex;
    }

    /// <summary>
    ///     The leader's place in <see cref="Members" />; 0 when there are none.
    /// </summary>
    public byte LeaderIndex { get; }

    public IReadOnlyList<PartyRosterEntry> Members { get; }

    /// <summary>
    ///     False for a malformed message: more than five members, a leader who is not one of them, or a member whose
    ///     name, job, level, presence, or map breaks its rule.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out PartyRoster? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyRoster)
            || !reader.TryReadByte(out byte count)
            || !reader.TryReadByte(out byte leader)
            || count > MaxMembers
            || (count == 0 ? leader != 0 : leader >= count))
        {
            return false;
        }

        var members = new PartyRosterEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
                || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string jobText)
                || !reader.TryReadUInt16(out ushort baseLevel)
                || !reader.TryReadByte(out byte inWorld)
                || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string mapText)
                || !CharacterNames.IsValid(name)
                || !JobDefinitionId.TryCreate(jobText, out JobDefinitionId job)
                || baseLevel == 0
                || inWorld > 1)
            {
                return false;
            }

            MapDefinitionId? map = null;
            if (inWorld == 1)
            {
                if (!MapDefinitionId.TryCreate(mapText, out MapDefinitionId shown))
                {
                    return false;
                }

                map = shown;
            }
            else if (mapText.Length != 0)
            {
                return false;
            }

            members[index] = new PartyRosterEntry(name, job, baseLevel, map);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new PartyRoster(leader, members);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + 2 * sizeof(byte);
        foreach (PartyRosterEntry member in Members)
        {
            length += WireText.GetEncodedLength(member.Name, ProtocolLimits.MaxCharacterNameBytes)
                + WireText.GetEncodedLength(member.Job.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + sizeof(ushort)
                + sizeof(byte)
                + WireText.GetEncodedLength(MapText(member), ProtocolLimits.MaxDefinitionIdBytes);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyRoster);
        writer.WriteByte((byte)Members.Count);
        writer.WriteByte(LeaderIndex);
        foreach (PartyRosterEntry member in Members)
        {
            writer.WriteString(member.Name, ProtocolLimits.MaxCharacterNameBytes);
            writer.WriteString(member.Job.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt16(member.BaseLevel);
            writer.WriteByte(member.IsInWorld ? (byte)1 : (byte)0);
            writer.WriteString(MapText(member), ProtocolLimits.MaxDefinitionIdBytes);
        }

        return writer.Position;
    }

    private static string MapText(PartyRosterEntry member)
    {
        return member.Map?.Value ?? string.Empty;
    }
}
}
