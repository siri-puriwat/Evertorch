namespace Evertorch.Server
{
/// <summary>
///     What a login and a password may be (Network Protocol §4).
/// </summary>
public static class AccountCredentialRules
{
    public const int MaxLoginLength = 64;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    /// <summary>
    ///     The stored form of <paramref name="login" />: 1 to 64 letters, digits, <c>_</c>, <c>-</c>, and <c>.</c>, in
    ///     lower case. Without a <c>:</c> it can never meet a development login (<c>dev:name</c>).
    /// </summary>
    public static bool TryNormalizeLogin(string? login, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(login) || login.Length > MaxLoginLength)
        {
            return false;
        }

        foreach (char character in login)
        {
            if (!IsLoginCharacter(character))
            {
                return false;
            }
        }

        normalized = login.ToLowerInvariant();
        return true;
    }

    /// <summary>
    ///     8 to 128 printable ASCII characters without spaces: the Windows console mangles other input and trims the
    ///     line it reads.
    /// </summary>
    public static bool IsValidPassword(string? password)
    {
        if (password == null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            return false;
        }

        foreach (char character in password)
        {
            if (character < '!' || character > '~')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLoginCharacter(char character)
    {
        return character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_'
            or '-'
            or '.';
    }
}
}
