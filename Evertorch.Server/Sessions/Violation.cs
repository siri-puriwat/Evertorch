namespace Evertorch.Server
{
/// <summary>
///     What the violation score counts (Network Protocol §11). Gameplay refusals, stale or dropped movement input, and
///     messages an honest client can send in a race are never violations.
/// </summary>
public enum Violation
{
    /// <summary>
    ///     A payload that is oversized, of an unknown or server-to-client opcode, on the wrong channel or delivery, or
    ///     that does not decode.
    /// </summary>
    Malformed = 0,

    /// <summary>
    ///     A hello after the first.
    /// </summary>
    RepeatedHello = 1,

    /// <summary>
    ///     A command whose sequence is not newer than the last one processed.
    /// </summary>
    StaleCommand = 2,

    /// <summary>
    ///     Input dropped because its peer was over its message budget (layer 1).
    /// </summary>
    InputRate = 3,

    /// <summary>
    ///     A command refused or dropped by its class's bucket (layer 2).
    /// </summary>
    CommandRate = 4
}
}
