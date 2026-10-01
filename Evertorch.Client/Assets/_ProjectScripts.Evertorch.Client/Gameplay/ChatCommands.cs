using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What a typed chat line asks for: a line to send, words the client answers itself, or nothing at all.
/// </summary>
public readonly struct ChatRequest
{
    private ChatRequest(ChatChannel channel, string recipient, string text, string? refusal, string? invitee = null)
    {
        Channel = channel;
        Recipient = recipient;
        Text = text;
        Refusal = refusal;
        Invitee = invitee;
    }

    /// <summary>
    ///     <see cref="ChatChannel.None" /> when nothing is sent.
    /// </summary>
    public ChatChannel Channel { get; }

    public string Recipient { get; }

    public string Text { get; }

    /// <summary>
    ///     What the client tells the player instead of sending; null when it says nothing.
    /// </summary>
    public string? Refusal { get; }

    /// <summary>
    ///     The player to invite to the party; null unless the line was <c>/invite Name</c>.
    /// </summary>
    public string? Invitee { get; }

    public static ChatRequest Send(ChatChannel channel, string recipient, string text)
    {
        return new ChatRequest(channel, recipient, text, null);
    }

    public static ChatRequest Refuse(string words)
    {
        return new ChatRequest(ChatChannel.None, string.Empty, string.Empty, words);
    }

    public static ChatRequest Nothing => new(ChatChannel.None, string.Empty, string.Empty, null);

    public static ChatRequest Invite(string name)
    {
        return new ChatRequest(ChatChannel.None, string.Empty, string.Empty, null, name);
    }
}

/// <summary>
///     The chat input's commands (Prototype Content §2): <c>/w Name text</c> whispers, <c>/r text</c> answers the last
///     whisper, <c>/p text</c> speaks to the party, <c>/invite Name</c> invites to it, and anything else is said
///     nearby. The client checks the name and
///     the text rules itself, so an honest player is never scored for a malformed line (Network Protocol §11).
/// </summary>
public static class ChatCommands
{
    public const string Usage = "Commands: /w Name text, /r text, /p text, /invite Name.";

    public static ChatRequest Parse(string typed, string? lastWhisperer, string? ownName)
    {
        if (typed == null)
        {
            throw new ArgumentNullException(nameof(typed));
        }

        if (!typed.StartsWith("/", StringComparison.Ordinal))
        {
            return Line(ChatChannel.Nearby, string.Empty, typed);
        }

        string command = FirstWord(typed, out string rest);
        if (string.Equals(command, "/w", StringComparison.OrdinalIgnoreCase))
        {
            string name = FirstWord(rest, out string text);
            return name.Length == 0 ? ChatRequest.Refuse("Whisper with /w Name text.") : Whisper(name, text, ownName);
        }

        if (string.Equals(command, "/r", StringComparison.OrdinalIgnoreCase))
        {
            return lastWhisperer == null
                ? ChatRequest.Refuse("No one has whispered to you.")
                : Whisper(lastWhisperer, rest, ownName);
        }

        if (string.Equals(command, "/p", StringComparison.OrdinalIgnoreCase))
        {
            return Line(ChatChannel.Party, string.Empty, rest);
        }

        if (string.Equals(command, "/invite", StringComparison.OrdinalIgnoreCase))
        {
            string name = FirstWord(rest, out _);
            return name.Length == 0 ? ChatRequest.Refuse("Invite with /invite Name.") : ChatRequest.Invite(name);
        }

        return ChatRequest.Refuse(Usage);
    }

    private static ChatRequest Whisper(string name, string text, string? ownName)
    {
        if (!CharacterNames.IsValid(name))
        {
            return ChatRequest.Refuse($"{name} is not online.");
        }

        if (string.Equals(name, ownName, StringComparison.OrdinalIgnoreCase))
        {
            return ChatRequest.Refuse("You cannot whisper to yourself.");
        }

        return Line(ChatChannel.Whisper, name, text);
    }

    // Text that breaks the rule, only spaces most likely, is not sent and says nothing.
    private static ChatRequest Line(ChatChannel channel, string recipient, string text)
    {
        return ChatText.IsValid(text) ? ChatRequest.Send(channel, recipient, text) : ChatRequest.Nothing;
    }

    private static string FirstWord(string text, out string rest)
    {
        string trimmed = text.TrimStart(' ');
        int space = trimmed.IndexOf(' ');
        if (space < 0)
        {
            rest = string.Empty;
            return trimmed;
        }

        rest = trimmed.Substring(space + 1);
        return trimmed.Substring(0, space);
    }
}
}
