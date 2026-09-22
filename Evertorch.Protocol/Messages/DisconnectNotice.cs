using System;

namespace Evertorch.Protocol
{
/// <summary>
///     The last message a server sends before it closes a connection. The text is safe to show a player and never
///     carries diagnostics.
/// </summary>
public sealed class DisconnectNotice
{
    public DisconnectNotice(DisconnectReason reason, string message)
    {
        Reason = reason;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public DisconnectReason Reason { get; }

    public string Message { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out DisconnectNotice? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.DisconnectNotice)
            || !reader.TryReadByte(out byte reasonValue)
            || !reader.TryReadString(ProtocolLimits.MaxNoticeMessageBytes, out string text)
            || !reader.IsAtEnd
            || !WireEnums.IsDefined((DisconnectReason)reasonValue))
        {
            return false;
        }

        message = new DisconnectNotice((DisconnectReason)reasonValue, text);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(byte)
            + WireText.GetEncodedLength(Message, ProtocolLimits.MaxNoticeMessageBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.DisconnectNotice);
        writer.WriteByte((byte)Reason);
        writer.WriteString(Message, ProtocolLimits.MaxNoticeMessageBytes);
        return writer.Position;
    }
}
}
