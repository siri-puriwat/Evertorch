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
    AttackEntity = 0x0006,
    CancelAction = 0x0007,
    Respawn = 0x000C,
    CreateCharacter = 0x000D,
    Logout = 0x000E,
    ServerHello = 0x8001,
    WorldEntered = 0x8003,
    EntitySpawn = 0x8004,
    EntityDespawn = 0x8005,
    EntitySnapshot = 0x8006,
    TargetChanged = 0x8007,
    AttackStarted = 0x8008,
    Damage = 0x8009,
    EntityDied = 0x800A,
    ItemDropped = 0x800D,
    DisconnectNotice = 0x8013,
    CharacterHealth = 0x8014,
    EntityRevived = 0x8015,
    CharacterList = 0x8016,
    CreateCharacterResult = 0x8017,
    LogoutComplete = 0x8019
}
}
