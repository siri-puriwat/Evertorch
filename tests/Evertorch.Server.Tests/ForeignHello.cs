using System;
using System.Buffers.Binary;
using System.Text;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A hello as another protocol version might write it: the frozen opcode and version first (Network Protocol §5),
///     then fields in an order this version does not use, padded to the length asked for.
/// </summary>
internal static class ForeignHello
{
    public static byte[] Encode(int protocolVersion, int length)
    {
        byte[] payload = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)MessageOpcode.ClientHello);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)protocolVersion);
        byte[] token = Encoding.UTF8.GetBytes("dev:future");
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)token.Length);
        token.CopyTo(payload, 6);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(6 + token.Length), 0x11326BD1);
        return payload;
    }
}
}
