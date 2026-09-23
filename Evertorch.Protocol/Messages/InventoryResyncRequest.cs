using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks for a new <see cref="InventorySnapshot" /> after the client saw an inventory revision it could not apply
///     (Network Protocol §12). It changes nothing, so it carries no command sequence.
/// </summary>
public readonly struct InventoryResyncRequest
{
    public const int EncodedLength = sizeof(ushort);

    public static bool TryRead(ReadOnlySpan<byte> source, out InventoryResyncRequest message)
    {
        message = default;
        var reader = new WireReader(source);
        return reader.TryReadOpcode(MessageOpcode.InventoryResyncRequest) && reader.IsAtEnd;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.InventoryResyncRequest);
        return writer.Position;
    }
}
}
