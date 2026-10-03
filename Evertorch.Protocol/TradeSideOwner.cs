namespace Evertorch.Protocol
{
/// <summary>
///     Whose offer a <see cref="TradeSide" /> shows (Network Protocol §6). Zero is never sent.
/// </summary>
public enum TradeSideOwner : byte
{
    None = 0,

    /// <summary>
    ///     The receiver's own offer, its rows named by their IDs.
    /// </summary>
    Own = 1,

    /// <summary>
    ///     The partner's offer, its rows without their IDs.
    /// </summary>
    Partner = 2
}
}
