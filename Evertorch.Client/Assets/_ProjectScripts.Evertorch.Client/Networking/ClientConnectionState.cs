namespace Evertorch.Client
{
public enum ClientConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    AwaitingHello = 2,

    /// <summary>
    ///     Signed in: the account's characters are listed, one can be created, and one can be entered.
    /// </summary>
    SelectingCharacter = 5,

    EnteringWorld = 3,
    InWorld = 4
}
}
