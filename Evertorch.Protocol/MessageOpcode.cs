namespace Evertorch.Protocol
{
/// <summary>
///     Stable wire identities. Client-to-server messages use 0x0001–0x7FFF and server-to-client messages use
///     0x8000–0xFFFF. A value is never reused with an incompatible meaning.
/// </summary>
public enum MessageOpcode : ushort
{
    None = 0,
    ClientHello = 0x0001,
    EnterWorldRequest = 0x0002,
    MoveInput = 0x0003,
    StopMovement = 0x0004,
    TargetEntity = 0x0005,
    ServerHello = 0x8001,
    WorldEntered = 0x8003,
    EntitySpawn = 0x8004,
    EntityDespawn = 0x8005,
    EntitySnapshot = 0x8006,
    DisconnectNotice = 0x8013
}
}
