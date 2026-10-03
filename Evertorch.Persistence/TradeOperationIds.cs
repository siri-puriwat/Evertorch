using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Evertorch.Persistence
{
/// <summary>
///     The operation ID of each ledger row a trade writes (Persistence §5): a name-based UUID, version 5, of the
///     character and the line within the trade's ID, so one trade's many rows each keep the ledger's unique ID and a
///     replay derives the same ones.
/// </summary>
public static class TradeOperationIds
{
    private const int VersionByte = 6;
    private const int VariantByte = 8;

    public static Guid For(Guid tradeId, long characterId, int line)
    {
        Span<byte> space = stackalloc byte[16];
        tradeId.TryWriteBytes(space, true, out _);
        byte[] name = Encoding.UTF8.GetBytes(
            characterId.ToString(CultureInfo.InvariantCulture) + ":" + line.ToString(CultureInfo.InvariantCulture));
        byte[] input = new byte[space.Length + name.Length];
        space.CopyTo(input);
        name.CopyTo(input, space.Length);

        // Version 5 is defined over SHA-1 (RFC 9562 §5.5); it names a row and protects nothing.
        byte[] hash = SHA1.HashData(input);
        hash[VersionByte] = (byte)((hash[VersionByte] & 0x0F) | 0x50);
        hash[VariantByte] = (byte)((hash[VariantByte] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), true);
    }
}
}
