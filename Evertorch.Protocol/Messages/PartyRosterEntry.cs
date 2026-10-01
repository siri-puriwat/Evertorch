using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One member as the party list shows it: its name, job, and base level, and its map while it is in the world.
/// </summary>
public readonly struct PartyRosterEntry
{
    public PartyRosterEntry(string name, JobDefinitionId job, ushort baseLevel, MapDefinitionId? map)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Job = job;
        BaseLevel = baseLevel;
        Map = map;
    }

    public string Name { get; }

    public JobDefinitionId Job { get; }

    public ushort BaseLevel { get; }

    /// <summary>
    ///     Null while the member is not in the world.
    /// </summary>
    public MapDefinitionId? Map { get; }

    public bool IsInWorld => Map != null;
}
}
