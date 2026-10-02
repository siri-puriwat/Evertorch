using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A boss appeared or fell on the receiver's map (Network Protocol §9): the boss, and on its fall the name of its
///     most valuable player, which the client puts into words.
/// </summary>
public sealed class BossAnnouncement
{
    public BossAnnouncement(BossAnnouncementKind kind, MonsterDefinitionId monster, string name)
    {
        if (monster == default)
        {
            throw new ArgumentException("A monster is required.", nameof(monster));
        }

        Kind = kind;
        Monster = monster;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public BossAnnouncementKind Kind { get; }

    public MonsterDefinitionId Monster { get; }

    /// <summary>
    ///     On a fall, the most valuable player's name, or empty when nobody earned it; always empty on an appearance.
    /// </summary>
    public string Name { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out BossAnnouncement? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.BossAnnouncement)
            || !reader.TryReadByte(out byte kindValue)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string monsterText)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.IsAtEnd
            || !MonsterDefinitionId.TryCreate(monsterText, out MonsterDefinitionId monster))
        {
            return false;
        }

        var kind = (BossAnnouncementKind)kindValue;
        bool isNameAllowed = name.Length == 0 || (kind == BossAnnouncementKind.Fell && CharacterNames.IsValid(name));
        if (!WireEnums.IsDefined(kind) || !isNameAllowed)
        {
            return false;
        }

        message = new BossAnnouncement(kind, monster, name);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(byte)
            + WireText.GetEncodedLength(Monster.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.BossAnnouncement);
        writer.WriteByte((byte)Kind);
        writer.WriteString(Monster.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        return writer.Position;
    }
}
}
