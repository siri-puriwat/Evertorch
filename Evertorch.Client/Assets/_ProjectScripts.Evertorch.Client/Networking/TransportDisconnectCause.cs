namespace Evertorch.Client
{
public enum TransportDisconnectCause
{
    None = 0,
    ClosedLocally = 1,
    ClosedByServer = 2,
    TimedOut = 3,
    ConnectionFailed = 4,
    Other = 5
}
}
