using System;
using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What a chat line is, which decides its colour.
/// </summary>
public enum ChatLineKind
{
    System,
    Nearby,
    Party,
    WhisperReceived,
    WhisperSent
}

/// <summary>
///     One line of the chat log, already in words.
/// </summary>
public readonly struct ChatLine
{
    public ChatLine(ChatLineKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public ChatLineKind Kind { get; }

    public string Text { get; }
}

/// <summary>
///     The chat log (Prototype Content §2): the last 50 lines said, whispered, and told by the client itself, in
///     words. It belongs to the client, not to a world, so a map change and a reconnect keep it.
/// </summary>
public sealed class ChatLog
{
    public const int Capacity = 50;

    private readonly List<ChatLine> m_lines = new();

    public IReadOnlyList<ChatLine> Lines => m_lines;

    /// <summary>
    ///     Who last whispered to this client, which a reply answers; null until someone has.
    /// </summary>
    public string? LastWhisperer { get; private set; }

    /// <summary>
    ///     Raised after every line added.
    /// </summary>
    public event Action? Changed;

    public static string Describe(ChatReceived line)
    {
        if (line == null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        switch (line.Channel)
        {
            case ChatChannel.Party:
                return $"[Party] {line.Name}: {line.Text}";
            case ChatChannel.Whisper:
                return $"From {line.Name}: {line.Text}";
            case ChatChannel.WhisperSent:
                return $"To {line.Name}: {line.Text}";
            default:
                return $"{line.Name}: {line.Text}";
        }
    }

    public void Add(ChatReceived line)
    {
        if (line == null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        ChatLineKind kind = line.Channel switch
        {
            ChatChannel.Party => ChatLineKind.Party,
            ChatChannel.Whisper => ChatLineKind.WhisperReceived,
            ChatChannel.WhisperSent => ChatLineKind.WhisperSent,
            _ => ChatLineKind.Nearby
        };
        if (kind == ChatLineKind.WhisperReceived)
        {
            LastWhisperer = line.Name;
        }

        Append(new ChatLine(kind, Describe(line)));
    }

    /// <summary>
    ///     A line the client tells the player itself: a refusal, a pickup, a purchase or a sale, a quest, or the build.
    /// </summary>
    public void AddSystem(string text)
    {
        Append(new ChatLine(ChatLineKind.System, text ?? throw new ArgumentNullException(nameof(text))));
    }

    private void Append(ChatLine line)
    {
        if (m_lines.Count == Capacity)
        {
            m_lines.RemoveAt(0);
        }

        m_lines.Add(line);
        Changed?.Invoke();
    }
}
}
