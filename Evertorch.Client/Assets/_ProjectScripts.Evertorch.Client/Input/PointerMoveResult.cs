namespace Evertorch.Client
{
public enum PointerMoveResult
{
    None = 0,
    OnControl = 1,
    MissedMap = 2,
    Refused = 3,
    Accepted = 4,

    /// <summary>The pointer was on an entity; nothing walks.</summary>
    Entity = 5,

    /// <summary>The pointer reached the map while nothing may walk: a skill was waiting for its target.</summary>
    OnGround = 6
}
}
