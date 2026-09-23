namespace Evertorch.Client
{
public enum TargetRequest
{
    None = 0,
    Next = 1,
    Previous = 2,
    Clear = 3,

    /// <summary>Attack the current target (gamepad West).</summary>
    Attack = 4
}
}
