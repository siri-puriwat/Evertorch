using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks for a new character on the signed-in account. The server applies the naming policy; the reader checks
///     only the length.
/// </summary>
public sealed class CreateCharacter
{
    public CreateCharacter(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CreateCharacter? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CreateCharacter)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new CreateCharacter(name);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CreateCharacter);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        return writer.Position;
    }
}
}
