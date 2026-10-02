namespace Evertorch.Protocol
{
/// <summary>
///     What a <see cref="BossAnnouncement" /> tells (Network Protocol §9): the boss appeared, at start-up or on its
///     return, or it fell. Zero is never sent.
/// </summary>
public enum BossAnnouncementKind : byte
{
    None = 0,
    Appeared = 1,
    Fell = 2
}
}
