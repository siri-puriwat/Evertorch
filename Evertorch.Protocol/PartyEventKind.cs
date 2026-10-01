namespace Evertorch.Protocol
{
/// <summary>
///     What a <see cref="PartyEvent" /> tells (Gameplay Systems §14), and whose name it carries: the inviter for an
///     invite; the invitee for a decline or an expiry; the member who joined, left, or was removed; the new leader; or
///     the member whose departure disbanded the party. Zero is never sent.
/// </summary>
public enum PartyEventKind : byte
{
    None = 0,
    Invited = 1,
    Declined = 2,
    Expired = 3,
    Joined = 4,
    Left = 5,
    Kicked = 6,
    LeaderChanged = 7,
    Disbanded = 8
}
}
