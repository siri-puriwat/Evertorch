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
    InWorld = 2
}
}
