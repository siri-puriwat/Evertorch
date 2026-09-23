namespace Evertorch.Protocol
{
public static class ProtocolConstants
{
    /// <summary>
    ///     Raised whenever a field's meaning, order, width, or required semantics change. Client and server must match.
    /// </summary>
    public const ushort ProtocolVersion = 6;

    /// <summary>
    ///     The first value of the server-to-client opcode range.
    /// </summary>
    public const ushort FirstServerOpcode = 0x8000;
}
}
