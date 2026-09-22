namespace Evertorch.Game
{
public static class DefinitionIdLimits
{
    /// <summary>
    ///     Longest definition ID, in characters. IDs are ASCII, so this is also their size in bytes on the wire.
    /// </summary>
    public const int MaxLength = 64;
}
}
