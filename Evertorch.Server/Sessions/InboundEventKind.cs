namespace Evertorch.Server
{
public enum InboundEventKind
{
    Connected = 0,
    Disconnected = 1,
    Hello = 2,
    EnterWorld = 3,

    /// <summary>
    ///     A movement intent. A request to stop is the same event with a zero direction.
    /// </summary>
    Move = 5,

    /// <summary>
    ///     A request to select a target; entity 0 clears it.
    /// </summary>
    Target = 6,

    /// <summary>
    ///     A request to attack <see cref="InboundEvent.Target" />, carrying a command sequence.
    /// </summary>
    Attack = 7,

    /// <summary>
    ///     A request to end auto-attack, carrying a command sequence.
    /// </summary>
    Cancel = 8,

    /// <summary>
    ///     A request to revive a dead character, carrying a command sequence.
    /// </summary>
    Respawn = 9,

    /// <summary>
    ///     The peer sent something that is not a well-formed client message on its proper channel.
    /// </summary>
    Malformed = 4
}
}
