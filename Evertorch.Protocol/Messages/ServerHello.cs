using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Sent once a <see cref="ClientHello" /> has been accepted. A rejected hello gets a
///     <see cref="DisconnectNotice" /> instead.
/// </summary>
public sealed class ServerHello
{
    public ServerHello(
        ushort protocolVersion,
        string serverBuildVersion,
        uint requiredClientContentVersion,
        uint serverTickRate,
        long serverTimeUnixMilliseconds)
    {
        ProtocolVersion = protocolVersion;
        ServerBuildVersion = serverBuildVersion ?? throw new ArgumentNullException(nameof(serverBuildVersion));
        RequiredClientContentVersion = requiredClientContentVersion;
        ServerTickRate = serverTickRate;
        ServerTimeUnixMilliseconds = serverTimeUnixMilliseconds;
    }

    public ushort ProtocolVersion { get; }

    public string ServerBuildVersion { get; }

    public uint RequiredClientContentVersion { get; }

    /// <summary>
    ///     Simulation ticks per second. The client runs its prediction at this rate.
    /// </summary>
    public uint ServerTickRate { get; }

    public long ServerTimeUnixMilliseconds { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out ServerHello? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ServerHello)
            || !reader.TryReadUInt16(out ushort protocolVersion)
            || !reader.TryReadString(ProtocolLimits.MaxBuildVersionBytes, out string serverBuildVersion)
            || !reader.TryReadUInt32(out uint requiredClientContentVersion)
            || !reader.TryReadUInt32(out uint serverTickRate)
            || !reader.TryReadInt64(out long serverTime)
            || !reader.IsAtEnd
            || serverTickRate == 0)
        {
            return false;
        }

        message = new ServerHello(
            protocolVersion,
            serverBuildVersion,
            requiredClientContentVersion,
            serverTickRate,
            serverTime);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(ushort)
            + WireText.GetEncodedLength(ServerBuildVersion, ProtocolLimits.MaxBuildVersionBytes)
            + sizeof(uint)
            + sizeof(uint)
            + sizeof(long);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ServerHello);
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteString(ServerBuildVersion, ProtocolLimits.MaxBuildVersionBytes);
        writer.WriteUInt32(RequiredClientContentVersion);
        writer.WriteUInt32(ServerTickRate);
        writer.WriteInt64(ServerTimeUnixMilliseconds);
        return writer.Position;
    }
}
}
