using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the character named <see cref="Partner" /> to trade (Gameplay Systems §16). The partner hears of it; it
///     waits 30 s for an answer.
/// </summary>
public sealed class TradeRequest
{
    public TradeRequest(string partner, uint commandSequence)
    {
        Partner = partner ?? throw new ArgumentNullException(nameof(partner));
        CommandSequence = commandSequence;
    }

    public string Partner { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a name that breaks the name rule.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out TradeRequest? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeRequest)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string partner)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || !CharacterNames.IsValid(partner))
        {
            return false;
        }

        message = new TradeRequest(partner, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + WireText.GetEncodedLength(Partner, ProtocolLimits.MaxCharacterNameBytes) + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeRequest);
        writer.WriteString(Partner, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
