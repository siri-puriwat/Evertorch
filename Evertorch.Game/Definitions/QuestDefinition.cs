namespace Evertorch.Game
{
/// <summary>
///     A quest (Gameplay Systems §2.2): the NPC that gives it, one kill objective, and a reward of base experience, job
///     experience, and coins.
/// </summary>
public sealed class QuestDefinition
{
    public QuestDefinition(
        QuestDefinitionId id,
        string displayName,
        NpcDefinitionId giver,
        MonsterDefinitionId monster,
        int count,
        int baseExperience,
        int jobExperience,
        int currency)
    {
        Id = id;
        DisplayName = displayName;
        Giver = giver;
        Monster = monster;
        Count = count;
        BaseExperience = baseExperience;
        JobExperience = jobExperience;
        Currency = currency;
    }

    public QuestDefinitionId Id { get; }

    public string DisplayName { get; }

    public NpcDefinitionId Giver { get; }

    /// <summary>
    ///     The monster whose kills count toward the quest.
    /// </summary>
    public MonsterDefinitionId Monster { get; }

    /// <summary>
    ///     How many kills make the quest ready to turn in.
    /// </summary>
    public int Count { get; }

    public int BaseExperience { get; }

    public int JobExperience { get; }

    /// <summary>
    ///     The coins the turn-in pays.
    /// </summary>
    public int Currency { get; }
}
}
