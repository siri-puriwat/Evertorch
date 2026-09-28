using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to spend stat points raising one primary statistic by <see cref="Steps" /> (Gameplay Systems §2). The
///     server refuses it whole or makes every raise; an accepted one answers with <see cref="CharacterSheet" />.
/// </summary>
public readonly struct AllocateStat
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(byte) + sizeof(uint);

    public AllocateStat(PrimaryStat stat, byte steps, uint commandSequence)
    {
        Stat = stat;
        Steps = steps;
        CommandSequence = commandSequence;
    }

    public PrimaryStat Stat { get; }

    /// <summary>
    ///     The raises to make, at least 1.
    /// </summary>
    public byte Steps { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a statistic outside STR to LUK and 0 steps.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out AllocateStat message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.AllocateStat)
            || !reader.TryReadByte(out byte stat)
            || !reader.TryReadByte(out byte steps)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || !WireEnums.IsDefined((PrimaryStat)stat)
            || steps == 0)
        {
            return false;
        }

        message = new AllocateStat((PrimaryStat)stat, steps, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.AllocateStat);
        writer.WriteByte((byte)Stat);
        writer.WriteByte(Steps);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
