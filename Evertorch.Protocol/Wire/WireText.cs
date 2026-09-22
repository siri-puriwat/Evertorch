using System;
using System.Text;

namespace Evertorch.Protocol
{
internal static class WireText
{
    public const int LengthPrefixBytes = sizeof(ushort);

    // Strict: malformed input throws instead of being replaced, so a bad string is rejected rather than altered.
    public static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int GetByteCount(string value, int maxBytes)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        int length = StrictUtf8.GetByteCount(value);
        if (length > maxBytes)
        {
            throw new ArgumentException("The string is longer than its wire limit of " + maxBytes + " bytes.");
        }

        return length;
    }

    public static int GetEncodedLength(string value, int maxBytes)
    {
        return LengthPrefixBytes + GetByteCount(value, maxBytes);
    }
}
}
