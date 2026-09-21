namespace Evertorch.Game
{
internal static class DefinitionIdFormat
{
    public static bool IsValid(string? value, string kindPrefix)
    {
        if (value == null || value.Length <= kindPrefix.Length + 1 || value.Length > DefinitionIdLimits.MaxLength)
        {
            return false;
        }

        if (string.CompareOrdinal(value, 0, kindPrefix, 0, kindPrefix.Length) != 0 || value[kindPrefix.Length] != '.')
        {
            return false;
        }

        bool isSegmentEmpty = false;
        for (int index = kindPrefix.Length; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '.')
            {
                if (isSegmentEmpty)
                {
                    return false;
                }

                isSegmentEmpty = true;
                continue;
            }

            if (!IsSegmentCharacter(character))
            {
                return false;
            }

            isSegmentEmpty = false;
        }

        return !isSegmentEmpty;
    }

    private static bool IsSegmentCharacter(char character)
    {
        return (character >= 'a' && character <= 'z')
            || (character >= '0' && character <= '9')
            || character == '_'
            || character == '-';
    }
}
}
