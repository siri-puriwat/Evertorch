using System;

namespace Evertorch.Protocol
{
/// <summary>
/// First message on a connection. The server checks every field before it binds a session; the token is a
/// credential and must never be logged.
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

    /// <summary>See <see cref="ContentVersionCodec"/>.</summary>
    public uint ClientContentVersion { get; }

    public string SessionToken { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out ClientHello? message)
    {
        message = null;
        WireReader reader = new WireReader(source);
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
        WireWriter writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ClientHello);
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteString(ClientBuildVersion, ProtocolLimits.MaxBuildVersionBytes);
        writer.WriteUInt32(ClientContentVersion);
        writer.WriteString(SessionToken, ProtocolLimits.MaxSessionTokenBytes);
        return writer.Position;
    }
}
}
