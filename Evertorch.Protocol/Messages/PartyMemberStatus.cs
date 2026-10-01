using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Another member's health and SP in thousandths of their maximums (Network Protocol §9), which reach the other
///     members of its party and nobody else, at most once a second per member.
/// </summary>
public sealed class PartyMemberStatus
{
    public PartyMemberStatus(string name, ushort healthPermille, ushort spiritPermille)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        HealthPermille = healthPermille;
        SpiritPermille = spiritPermille;
    }

    public string Name { get; }

    public ushort HealthPermille { get; }

    public ushort SpiritPermille { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule and a value above 1000.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out PartyMemberStatus? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyMemberStatus)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.TryReadUInt16(out ushort health)
            || !reader.TryReadUInt16(out ushort spirit)
            || !reader.IsAtEnd
            || health > HealthRatio.Full
            || spirit > HealthRatio.Full
            || !CharacterNames.IsValid(name))
        {
            return false;
        }

        message = new PartyMemberStatus(name, health, spirit);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes)
            + 2 * sizeof(ushort);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyMemberStatus);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteUInt16(HealthPermille);
        writer.WriteUInt16(SpiritPermille);
        return writer.Position;
    }
}
}
