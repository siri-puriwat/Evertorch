namespace Evertorch.Protocol
{
/// <summary>
/// Content versions are 16 hexadecimal digits in manifests but travel as 32 bits in the handshake. The wire value
/// is the first eight digits. It gates compatibility, not security, so 32 bits are enough to tell builds apart.
/// </summary>
public static class ContentVersionCodec
{
    private const int ManifestDigits = 16;
    private const int WireDigits = 8;

    public static bool TryToWire(string? contentVersion, out uint wireVersion)
    {
        wireVersion = 0;
        if (contentVersion == null || contentVersion.Length != ManifestDigits)
        {
            return false;
        }

        uint value = 0;
        for (int index = 0; index < ManifestDigits; index++)
        {
            int digit = HexValue(contentVersion[index]);
            if (digit < 0)
            {
                return false;
            }

            if (index < WireDigits)
            {
                value = (value << 4) | (uint)digit;
            }
        }

        wireVersion = value;
        return true;
    }

    private static int HexValue(char character)
    {
        if (character >= '0' && character <= '9')
        {
            return character - '0';
        }

        return character >= 'a' && character <= 'f' ? character - 'a' + 10 : -1;
    }
}
}
