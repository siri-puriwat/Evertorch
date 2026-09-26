namespace Evertorch.Game
{
/// <summary>
///     NPC interaction as the tools, the server, and the client share it (Gameplay Systems §6.1).
/// </summary>
public static class NpcInteraction
{
    /// <summary>
    ///     How far from an NPC, measured on the ground from centre to centre, a character may use its services. The
    ///     server adds its attack range tolerance, which absorbs the client's view of positions.
    /// </summary>
    public const float Range = 3f;
}
}
