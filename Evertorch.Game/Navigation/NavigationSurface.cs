namespace Evertorch.Game
{
/// <summary>
///     What occupies one navigation cell. Only <see cref="Floor" /> can be walked on; the other kinds differ in how a
///     client presents them, never in how they block.
/// </summary>
public enum NavigationSurface
{
    Floor = 0,
    Wall = 1,
    Obstacle = 2,
    NpcMarker = 3,
    Gate = 4
}
}
