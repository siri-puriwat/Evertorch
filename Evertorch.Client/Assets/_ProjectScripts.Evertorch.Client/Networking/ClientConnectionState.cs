namespace Evertorch.Client
{
public enum ClientConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    AwaitingHello = 2,
    EnteringWorld = 3,
    InWorld = 4,
}
}
