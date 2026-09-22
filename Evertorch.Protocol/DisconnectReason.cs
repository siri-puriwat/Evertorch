namespace Evertorch.Protocol
{
/// <summary>
///     Stable machine-readable reasons a server closes a connection. Zero is never sent.
/// </summary>
public enum DisconnectReason : byte
{
    None = 0,
    ProtocolMismatch = 1,
    ClientBuildUnsupported = 2,
    ContentUpdateRequired = 3,
    AuthenticationFailed = 4,
    SessionExpired = 5,
    SessionReplaced = 6,
    ServerFull = 7,
    ServerNotReady = 8,
    RateLimited = 9,
    Maintenance = 10,
    Kicked = 11,
    InternalError = 12
}
}
