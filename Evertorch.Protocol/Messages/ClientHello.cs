using System;

namespace Evertorch.Protocol
{
/// <summary>
///     First message on a connection. The server checks every field before it binds a session; the token is a
///     credential and must never be logged.
/// </summary>
public sealed class ClientHello
{
    public ClientHello(
        ushort protocolVersion,
        string clientBuildVersion,
        uint clientContentVersion,
        string sessionToken)
    {
        ProtocolVersion = protocolVersion;
        ClientBuildVersion = clientBuildVersion ?? throw new ArgumentNullException(nameof(clientBuildVersion));
        ClientContentVersion = clientContentVersion;
        SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
    }

    public ushort ProtocolVersion { get; }

    public string ClientBuildVersion { get; }

    /// <summary>See <see cref="ContentVersionCodec" />.</summary>
    public uint ClientContentVersion { get; }

    public string SessionToken { get; }

    /// <summary>
    ///     Reads only the opcode and the protocol version, which lead the hello in every protocol version (Network
    ///     Protocol §5), so a server can tell a client of another version <c>ProtocolMismatch</c> however the rest of
    ///     that hello is laid out.
    /// </summary>
    public static bool TryReadProtocolVersion(ReadOnlySpan<byte> source, out ushort protocolVersion)
    {
        protocolVersion = 0;
        var reader = new WireReader(source);
        return reader.TryReadOpcode(MessageOpcode.ClientHello) && reader.TryReadUInt16(out protocolVersion);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out ClientHello? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ClientHello)
            || !reader.TryReadUInt16(out ushort protocolVersion)
            || !reader.TryReadString(ProtocolLimits.MaxBuildVersionBytes, out string clientBuildVersion)
            || !reader.TryReadUInt32(out uint clientContentVersion)
            || !reader.TryReadString(ProtocolLimits.MaxSessionTokenBytes, out string sessionToken)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new ClientHello(protocolVersion, clientBuildVersion, clientContentVersion, sessionToken);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(ushort)
            + WireText.GetEncodedLength(ClientBuildVersion, ProtocolLimits.MaxBuildVersionBytes)
            + sizeof(uint)
            + WireText.GetEncodedLength(SessionToken, ProtocolLimits.MaxSessionTokenBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ClientHello);
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteString(ClientBuildVersion, ProtocolLimits.MaxBuildVersionBytes);
        writer.WriteUInt32(ClientContentVersion);
        writer.WriteString(SessionToken, ProtocolLimits.MaxSessionTokenBytes);
        return writer.Position;
    }
}
}
