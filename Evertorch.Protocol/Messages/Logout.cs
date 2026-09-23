using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to leave the world for character selection. The server writes a final checkpoint first and answers with
///     <see cref="LogoutComplete" />; the connection stays open.
/// </summary>
public readonly struct Logout
{
    public const int EncodedLength = sizeof(ushort) + sizeof(uint);

    public Logout(uint commandSequence)
    {
        CommandSequence = commandSequence;
    }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out Logout message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.Logout)
            || !reader.TryReadUInt32(out uint commandSequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new Logout(commandSequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.Logout);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
