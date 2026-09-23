using System;

namespace Evertorch.Protocol
{
/// <summary>
///     The character has left the world and its checkpoint is written; a <see cref="CharacterList" /> follows.
/// </summary>
public readonly struct LogoutComplete
{
    public const int EncodedLength = sizeof(ushort);

    public static bool TryRead(ReadOnlySpan<byte> source, out LogoutComplete message)
    {
        message = default;
        var reader = new WireReader(source);
        return reader.TryReadOpcode(MessageOpcode.LogoutComplete) && reader.IsAtEnd;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.LogoutComplete);
        return writer.Position;
    }
}
}
