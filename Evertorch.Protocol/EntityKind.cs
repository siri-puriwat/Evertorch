namespace Evertorch.Protocol
{
/// <summary>
/// What a spawned entity is, which also decides the kind of definition ID it carries. Zero is never sent.
/// </summary>
public enum EntityKind : byte
{
    None = 0,

    /// <summary>Carries a job definition ID.</summary>
    Player = 1,
}
}
