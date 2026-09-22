namespace Evertorch.Protocol
{
/// <summary>
///     Why an entity stopped being visible to this client. Zero is never sent.
/// </summary>
public enum DespawnReason : byte
{
    None = 0,

    /// <summary>The entity still exists but left this client's area of interest.</summary>
    OutOfRange = 1,

    /// <summary>The entity left the world.</summary>
    Removed = 2
}
}
