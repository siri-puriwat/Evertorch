using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A chat line delivered (Gameplay Systems §15): said nearby by <see cref="Speaker" />, said to the party, a whisper
///     received, or the speaker's own whisper as sent. <see cref="Name" /> is the speaker's, or for a whisper sent the
///     recipient's.
/// </summary>
public sealed class ChatReceived
{
    public ChatReceived(ChatChannel channel, EntityId speaker, string name, string text)
    {
        Channel = channel;
        Speaker = speaker;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public ChatChannel Channel { get; }

    /// <summary>
    ///     The speaker's entity for a line said nearby, which shows over it; default for every other channel.
    /// </summary>
    public EntityId Speaker { get; }

    public string Name { get; }

    public string Text { get; }

    /// <summary>
    ///     False for a malformed message: an unknown channel, a speaker missing from a line said nearby or given for
    ///     another channel, a name that breaks the name rule, or text that breaks the chat text rule.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out ChatReceived? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ChatReceived)
            || !reader.TryReadByte(out byte channelValue)
            || !reader.TryReadInt64(out long speaker)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.TryReadString(ProtocolLimits.MaxChatTextBytes, out string text)
            || !reader.IsAtEnd)
        {
            return false;
        }

        var channel = (ChatChannel)channelValue;
        if (!WireEnums.IsDefined(channel)
            || (channel == ChatChannel.Nearby ? speaker <= 0 : speaker != 0)
            || !CharacterNames.IsValid(name)
            || !ChatText.IsValid(text))
        {
            return false;
        }

        message = new ChatReceived(channel, new EntityId(speaker), name, text);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(byte)
            + sizeof(long)
            + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes)
            + WireText.GetEncodedLength(Text, ProtocolLimits.MaxChatTextBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ChatReceived);
        writer.WriteByte((byte)Channel);
        writer.WriteInt64(Speaker.Value);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        writer.WriteString(Text, ProtocolLimits.MaxChatTextBytes);
        return writer.Position;
    }
}
}
