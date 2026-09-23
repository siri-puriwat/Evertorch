namespace Evertorch.Server
{
/// <summary>
///     The naming policy (Persistence §4): 4 to 23 characters of A–Z, a–z, and 0–9. Uniqueness is by the lower-case
///     form and is the database's to decide.
/// </summary>
public static class CharacterNamePolicy
{
    public const int MinLength = 4;
    public const int MaxLength = 23;

    public static bool IsValid(string name)
    {
        if (name.Length < MinLength || name.Length > MaxLength)
        {
            return false;
        }

        foreach (char character in name)
        {
            bool isAllowed = (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9');
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
}
