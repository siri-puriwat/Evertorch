namespace Evertorch.Protocol
{
/// <summary>
///     Logical streams with independent ordering, so a lost snapshot never delays newer input and neither waits behind
///     reliable traffic. The values are the transport channel numbers.
/// </summary>
public enum ProtocolChannel : byte
{
    Control = 0,
    Input = 1,
    State = 2
}
}
