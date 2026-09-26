using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One quest an NPC gives (Network Protocol §6): the monster to kill, how many, and the reward.
/// </summary>
public readonly struct NpcQuestOffer
{
    public NpcQuestOffer(
        QuestDefinitionId quest,
        MonsterDefinitionId monster,
        ushort count,
        ulong baseExperience,
        uint coins)
    {
        Quest = quest;
        Monster = monster;
        Count = count;
        BaseExperience = baseExperience;
        Coins = coins;
    }

    public QuestDefinitionId Quest { get; }

    public MonsterDefinitionId Monster { get; }

    public ushort Count { get; }

    public ulong BaseExperience { get; }

    public uint Coins { get; }
}
}
