namespace Evertorch.Server
{
public enum SessionState
{
    AwaitingHello = 0,

    /// <summary>
    ///     The hello was compatible and its token well formed; the account is being looked up.
    /// </summary>
    Authenticating = 3,

    Authenticated = 1,

    /// <summary>
    ///     Signed in and waiting for the chosen character to load from the database.
    /// </summary>
    EnteringWorld = 4,

    InWorld = 2
}
}
