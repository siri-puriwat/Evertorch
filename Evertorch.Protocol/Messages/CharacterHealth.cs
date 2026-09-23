using System;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver's own character's exact HP, sent to its owner alone after every change and after a revival.
/// </summary>
public readonly struct CharacterHealth
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(uint);

    public CharacterHealth(uint current, uint maximum)
    {
        Current = current;
        Maximum = maximum;
    }

    public uint Current { get; }

    public uint Maximum { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CharacterHealth message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CharacterHealth)
            || !reader.TryReadUInt32(out uint current)
            || !reader.TryReadUInt32(out uint maximum)
            || !reader.IsAtEnd
            || maximum == 0
            || current > maximum)
        {
            return false;
        }

        message = new CharacterHealth(current, maximum);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CharacterHealth);
        writer.WriteUInt32(Current);
        writer.WriteUInt32(Maximum);
        return writer.Position;
    }
}
}
