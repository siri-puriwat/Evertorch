using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What the player is told when the connection closes, and whether connecting again can help. The words are the
///     client's own, so they can be translated with the rest of its UI; the server adds text only for operator
///     context, such as the reason given for a shutdown (Network Protocol §5).
/// </summary>
public static class DisconnectMessages
{
    public static string Describe(DisconnectNotice? notice, TransportDisconnectCause cause)
    {
        if (notice == null)
        {
            return ForCause(cause);
        }

        string operatorText = notice.Message.Trim();
        string text = ForReason(notice.Reason);
        return operatorText.Length == 0 ? text : $"{text} The server says: {operatorText}";
    }

    /// <summary>
    ///     False after a refusal that only an update or another sign-in can cure. Nothing reconnects by itself: after
    ///     <see cref="DisconnectReason.SessionReplaced" /> that would make two clients of one character replace each other
    ///     forever.
    /// </summary>
    public static bool CanReconnect(DisconnectNotice? notice)
    {
        if (notice == null)
        {
            return true;
        }

        switch (notice.Reason)
        {
            case DisconnectReason.ProtocolMismatch:
            case DisconnectReason.ClientBuildUnsupported:
            case DisconnectReason.ContentUpdateRequired:
            case DisconnectReason.AuthenticationFailed:
                return false;
            default:
                return true;
        }
    }

    public static string ForReason(DisconnectReason reason)
    {
        switch (reason)
        {
            case DisconnectReason.ProtocolMismatch:
                return "This game speaks another version of the server's protocol. Update the game to play here.";
            case DisconnectReason.ClientBuildUnsupported:
                return "The server does not support this version of the game. Update the game to play here.";
            case DisconnectReason.ContentUpdateRequired:
                return "The game's data is out of date for this server. Update the game to play here.";
            case DisconnectReason.AuthenticationFailed:
                return "Sign-in failed. Check the login and password and sign in again.";
            case DisconnectReason.SessionExpired:
                return "The sign-in expired. Sign in again.";
            case DisconnectReason.SessionReplaced:
                return "This character was entered from somewhere else.";
            case DisconnectReason.ServerFull:
                return "The server is full. Try again in a moment.";
            case DisconnectReason.ServerNotReady:
                return "The server is not ready for players yet. Try again in a moment.";
            case DisconnectReason.RateLimited:
                return "Too much was sent too quickly. Wait a moment, then connect again.";
            case DisconnectReason.Maintenance:
                return "The server is closed for maintenance.";
            case DisconnectReason.Kicked:
                return "The server removed this client from the game.";
            case DisconnectReason.InternalError:
                return "The server ran into a problem and closed the connection.";
            default:
                return "The connection closed.";
        }
    }

    public static string ForCause(TransportDisconnectCause cause)
    {
        switch (cause)
        {
            case TransportDisconnectCause.ClosedLocally:
                return "Disconnected.";
            case TransportDisconnectCause.ClosedByServer:
                return "The server closed the connection.";
            case TransportDisconnectCause.TimedOut:
                return "The connection to the server was lost.";
            case TransportDisconnectCause.ConnectionFailed:
                return "The server could not be reached.";
            default:
                return "The connection closed.";
        }
    }
}
}
