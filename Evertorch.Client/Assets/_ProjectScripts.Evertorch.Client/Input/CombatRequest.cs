namespace Evertorch.Client
{
public enum CombatRequest
{
    None = 0,
    Next = 1,
    Previous = 2,
    Clear = 3,

    /// <summary>Attack the current target (gamepad West).</summary>
    Attack = 4,

    /// <summary>Come back to life at the map's spawn point; only asked for while dead.</summary>
    Respawn = 5,

    /// <summary>Pick up the nearest drop (F, gamepad North).</summary>
    Pickup = 6,

    /// <summary>Walk up to the nearest NPC in view and open its window (Prototype Content §4).</summary>
    Talk = 7
}
}
