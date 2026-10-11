namespace Evertorch.Game
{
/// <summary>
///     What a failed refine does to its item, set per refine table (Gameplay Systems §11.6); the values are the ones on
///     the wire.
/// </summary>
public enum RefineFailure : byte
{
    /// <summary>
    ///     The item is destroyed: the default.
    /// </summary>
    Destroy = 1,

    /// <summary>
    ///     The item falls by the table's levels, never below +0.
    /// </summary>
    Downgrade = 2,

    /// <summary>
    ///     The item stays as it was.
    /// </summary>
    Keep = 3
}
}
