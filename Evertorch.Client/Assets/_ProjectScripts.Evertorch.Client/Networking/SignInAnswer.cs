namespace Evertorch.Client
{
/// <summary>
///     What the gateway answered a sign-in (Network Protocol §4): where to connect and the token for the hello. It is
///     checked before anything uses it, so a garbled answer is a failed sign-in, never a connection to nowhere.
/// </summary>
public sealed class SignInAnswer
{
    public const string UdpTransport = "udp";
    public const string WebSocketTransport = "websocket";
    public const int TokenLength = 43;

    private const int MaxHostLength = 253;

    private SignInAnswer(string host, int port, string token)
    {
        Host = host;
        Port = port;
        Token = token;
    }

    public string Host { get; }

    public int Port { get; }

    /// <summary>
    ///     A secret: held in memory for the hello and a reconnect, never shown or logged.
    /// </summary>
    public string Token { get; }

    /// <param name="requested">The one transport the sign-in offered; the answer must name it.</param>
    public static bool TryCreate(
        string requested,
        string? transport,
        string? host,
        int port,
        string? token,
        out SignInAnswer? answer)
    {
        answer = null;
        if (transport != requested
            || !IsHost(host)
            || port < 1
            || port > 65535
            || token == null
            || token.Length != TokenLength
            || !IsBase64Url(token))
        {
            return false;
        }

        answer = new SignInAnswer(host!, port, token);
        return true;
    }

    private static bool IsHost(string? host)
    {
        if (string.IsNullOrEmpty(host) || host!.Length > MaxHostLength)
        {
            return false;
        }

        foreach (char character in host)
        {
            if (character <= ' ' || character > '~' || character == '/' || character == '@')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBase64Url(string token)
    {
        foreach (char character in token)
        {
            bool isAllowed = (character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '-'
                || character == '_';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
}
