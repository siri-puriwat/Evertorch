using System;

namespace Evertorch.Persistence
{
/// <summary>
///     A character's place in a party (Persistence §4), keyed by the character, which belongs to one party at most.
///     <see cref="JoinOrder" /> decides who leads when the leader leaves.
/// </summary>
internal sealed class PartyMemberRow
{
    public long PartyId { get; set; }

    public long CharacterId { get; set; }

    public DateTime JoinedAt { get; set; }

    public int JoinOrder { get; set; }
}
}
