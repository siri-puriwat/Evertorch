namespace Evertorch.Server
{
public enum InboundEventKind
{
    Connected = 0,
    Disconnected = 1,
    Hello = 2,
    EnterWorld = 3,

    /// <summary>
    /// The peer sent something that is not a well-formed client message on its proper channel.
    /// </summary>
    Malformed = 4,
}
}
