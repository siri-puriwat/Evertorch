using System;

namespace Evertorch.Persistence
{
/// <summary>
///     A party (Persistence §4): its leader, who must be one of its members.
/// </summary>
internal sealed class PartyRow
{
    public long Id { get; set; }

    public long LeaderCharacterId { get; set; }

    public DateTime CreatedAt { get; set; }

    public int Version { get; set; }
}
}
