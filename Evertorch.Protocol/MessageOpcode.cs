namespace Evertorch.Protocol
{
/// <summary>
/// Stable wire identities. Client-to-server messages use 0x0001–0x7FFF and server-to-client messages use
/// 0x8000–0xFFFF. A value is never reused with an incompatible meaning.
/// </summary>
public enum MessageOpcode : ushort
{
    None = 0,
    TargetEntity = 0x0005,
}
}
