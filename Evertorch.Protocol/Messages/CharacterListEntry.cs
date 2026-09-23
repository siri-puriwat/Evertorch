using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One character of the signed-in account, as the selection screen shows it.
/// </summary>
public readonly struct CharacterListEntry
{
    public CharacterListEntry(CharacterId character, string name, JobDefinitionId job, ushort baseLevel)
    {
        Character = character;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Job = job;
        BaseLevel = baseLevel;
    }

    public CharacterId Character { get; }

    public string Name { get; }

    public JobDefinitionId Job { get; }

    public ushort BaseLevel { get; }
}
}
