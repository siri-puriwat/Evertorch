using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A chat line said nearby, to the party, or in a whisper to the character named <see cref="Recipient" />
///     (Gameplay Systems §15). A refusal is <see cref="CommandRejected" /> with its sequence: 1 for a whisper that
///     finds no one, 3 for party chat without a party or for chat while logging out.
/// </summary>
public sealed class ChatSend
{
    public ChatSend(ChatChannel channel, string recipient, string text, uint commandSequence)
    {
        Channel = channel;
        Recipient = recipient ?? throw new ArgumentNullException(nameof(recipient));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        CommandSequence = commandSequence;
    }

    public ChatChannel Channel { get; }

    /// <summary>
    ///     The character a whisper goes to; empty for every other channel.
    /// </summary>
    public string Recipient { get; }

    public string Text { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message: a channel a client may not send, a recipient that breaks the name rule or is
    ///     given for another channel than a whisper, or text that breaks the chat text rule.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out ChatSend? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ChatSend)
            || !reader.TryReadByte(out byte channelValue)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string recipient)
            || !reader.TryReadString(ProtocolLimits.MaxChatTextBytes, out string text)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        var channel = (ChatChannel)channelValue;
        if (!WireEnums.IsSendable(channel)
            || !IsRecipientValid(channel, recipient)
            || !ChatText.IsValid(text))
        {
            return false;
        }

        message = new ChatSend(channel, recipient, text, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(byte)
            + WireText.GetEncodedLength(Recipient, ProtocolLimits.MaxCharacterNameBytes)
            + WireText.GetEncodedLength(Text, ProtocolLimits.MaxChatTextBytes)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ChatSend);
        writer.WriteByte((byte)Channel);
        writer.WriteString(Recipient, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteString(Text, ProtocolLimits.MaxChatTextBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }

    private static bool IsRecipientValid(ChatChannel channel, string recipient)
    {
        return channel == ChatChannel.Whisper ? CharacterNames.IsValid(recipient) : recipient.Length == 0;
    }
}
}
