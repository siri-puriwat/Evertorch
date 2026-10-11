namespace Evertorch.Game
{
/// <summary>
///     What one refine did to its item (Gameplay Systems §11.6); the values are the ones on the wire.
/// </summary>
public enum RefineOutcomeKind : byte
{
    None = 0,

    /// <summary>
    ///     The item rose one level.
    /// </summary>
    Refined = 1,

    /// <summary>
    ///     The failure destroyed the item.
    /// </summary>
    Destroyed = 2,

    /// <summary>
    ///     The failure lowered the item by its table's levels, never below +0.
    /// </summary>
    Lowered = 3,

    /// <summary>
    ///     The failure left the item as it was.
    /// </summary>
    Unchanged = 4
}
}
