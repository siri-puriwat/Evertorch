using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to enter the world as one character. The server decides whether the session may control it.
/// </summary>
public readonly struct EnterWorldRequest
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long);

    public EnterWorldRequest(CharacterId character)
    {
        Character = character;
    }

    public CharacterId Character { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out EnterWorldRequest message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EnterWorldRequest)
            || !reader.TryReadInt64(out long character)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new EnterWorldRequest(new CharacterId(character));
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EnterWorldRequest);
        writer.WriteInt64(Character.Value);
        return writer.Position;
    }
}
}
