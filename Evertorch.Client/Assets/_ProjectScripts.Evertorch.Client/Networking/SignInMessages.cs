namespace Evertorch.Client
{
/// <summary>
///     The client's words for a sign-in that did not give a token (Network Protocol §4). A server that cannot be
///     reached and one whose certificate is not trusted share one message, because a web build cannot tell them apart.
/// </summary>
public static class SignInMessages
{
    public const string SigningIn = "Signing in";

    public const string MissingCredentials = "Enter a login and a password.";

    public const string UnreadableAnswer = "The server's answer could not be read. Update the game to play here.";

    /// <param name="status">The HTTP status the gateway answered, or 0 when no answer came.</param>
    public static string ForStatus(long status)
    {
        switch (status)
        {
            case 0:
                return "The server could not be reached, or its certificate is not trusted.";
            case 400:
                return "The server could not read the sign-in. Update the game to play here.";
            case 401:
                return "Wrong login or password.";
            case 429:
                return "Too many sign-ins. Wait a minute and try again.";
            case 503:
                return "The server is not ready for players yet. Try again in a moment.";
            default:
                return $"The sign-in failed (HTTP {status}).";
        }
    }
}
}
