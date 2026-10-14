using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Another member's health and SP in thousandths of their maximums and its ailments (Network Protocol §9), which
///     reach the other members of its party and nobody else, at most once a second per member.
/// </summary>
public sealed class PartyMemberStatus
{
    /// <summary>
    ///     The ailments' bits: bit 0 poison, then stun, freeze, sleep, silence, and bit 5 blind.
    /// </summary>
    public const byte KnownAilments = 0x3F;

    public PartyMemberStatus(string name, ushort healthPermille, ushort spiritPermille, byte ailments = 0)
    {
        if ((ailments & ~KnownAilments) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ailments), "Only bits 0 to 5 name an ailment.");
        }

        Name = name ?? throw new ArgumentNullException(nameof(name));
        HealthPermille = healthPermille;
        SpiritPermille = spiritPermille;
        Ailments = ailments;
    }

    public string Name { get; }

    public ushort HealthPermille { get; }

    public ushort SpiritPermille { get; }

    /// <summary>
    ///     The member's ailments, one bit each (<see cref="KnownAilments" />).
    /// </summary>
    public byte Ailments { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule, a value above 1000, and an
    ///     ailment bit above 5.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out PartyMemberStatus? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.PartyMemberStatus)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.TryReadUInt16(out ushort health)
            || !reader.TryReadUInt16(out ushort spirit)
            || !reader.TryReadByte(out byte ailments)
            || !reader.IsAtEnd
            || health > HealthRatio.Full
            || spirit > HealthRatio.Full
            || (ailments & ~KnownAilments) != 0
            || !CharacterNames.IsValid(name))
        {
            return false;
        }

        message = new PartyMemberStatus(name, health, spirit, ailments);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes)
            + 2 * sizeof(ushort)
            + sizeof(byte);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.PartyMemberStatus);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteUInt16(HealthPermille);
        writer.WriteUInt16(SpiritPermille);
        writer.WriteByte(Ailments);
        return writer.Position;
    }
}
}
