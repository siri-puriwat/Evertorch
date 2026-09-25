using System;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver's own character's exact HP and SP, sent to its owner alone after every change of either and
///     after a revival.
/// </summary>
public readonly struct CharacterHealth
{
    public const int EncodedLength = sizeof(ushort) + 4 * sizeof(uint);

    public CharacterHealth(uint current, uint maximum, uint currentSpirit, uint maximumSpirit)
    {
        Current = current;
        Maximum = maximum;
        CurrentSpirit = currentSpirit;
        MaximumSpirit = maximumSpirit;
    }

    public uint Current { get; }

    public uint Maximum { get; }

    public uint CurrentSpirit { get; }

    /// <summary>
    ///     May be 0: a job can have no SP.
    /// </summary>
    public uint MaximumSpirit { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CharacterHealth message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CharacterHealth)
            || !reader.TryReadUInt32(out uint current)
            || !reader.TryReadUInt32(out uint maximum)
            || !reader.TryReadUInt32(out uint currentSpirit)
            || !reader.TryReadUInt32(out uint maximumSpirit)
            || !reader.IsAtEnd
            || maximum == 0
            || current > maximum
            || currentSpirit > maximumSpirit)
        {
            return false;
        }

        message = new CharacterHealth(current, maximum, currentSpirit, maximumSpirit);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CharacterHealth);
        writer.WriteUInt32(Current);
        writer.WriteUInt32(Maximum);
        writer.WriteUInt32(CurrentSpirit);
        writer.WriteUInt32(MaximumSpirit);
        return writer.Position;
    }
}
}
