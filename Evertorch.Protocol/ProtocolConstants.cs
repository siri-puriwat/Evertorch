namespace Evertorch.Protocol
{
public static class ProtocolConstants
{
    /// <summary>
    ///     Raised whenever a field's meaning, order, width, or required semantics change. Client and server must match.
    /// </summary>
    public const ushort ProtocolVersion = 16;

    /// <summary>
    ///     The build a client reports in its hello, and the one a server admits unless configured otherwise
    ///     (Network Protocol §5). One constant, so the two cannot drift apart by hand.
    /// </summary>
    public const string BuildVersion = "0.2.0-dev";

    /// <summary>
    ///     The first value of the server-to-client opcode range.
    /// </summary>
    public const ushort FirstServerOpcode = 0x8000;
}
}
