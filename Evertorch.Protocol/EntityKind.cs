namespace Evertorch.Protocol
{
/// <summary>
///     What a spawned entity is, which also decides the kind of definition ID it carries. Zero is never sent.
/// </summary>
public enum EntityKind : byte
{
    None = 0,

    /// <summary>Carries a job definition ID.</summary>
    Player = 1,

    /// <summary>Carries a monster definition ID.</summary>
    Monster = 2,

    /// <summary>An item lying on the ground; carries an item definition ID.</summary>
    ItemDrop = 3
}
}
