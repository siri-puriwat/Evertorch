namespace Evertorch.Server
{
/// <summary>
/// Totals since the transport started.
/// </summary>
public readonly struct TransportStatistics
{
    public TransportStatistics(
        long bytesReceived,
        long bytesSent,
        long packetsReceived,
        long packetsSent,
        long packetsLost)
    {
        BytesReceived = bytesReceived;
        BytesSent = bytesSent;
        PacketsReceived = packetsReceived;
        PacketsSent = packetsSent;
        PacketsLost = packetsLost;
    }

    public long BytesReceived { get; }

    public long BytesSent { get; }

    public long PacketsReceived { get; }

    public long PacketsSent { get; }

    /// <summary>
    /// Reliable packets the transport had to resend because no acknowledgement arrived.
    /// </summary>
    public long PacketsLost { get; }
}
}
