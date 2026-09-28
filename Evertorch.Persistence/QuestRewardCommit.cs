using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One quest's turn-in to commit (Persistence §5): the reward's coins, and the level and experience and the job
///     level and job experience the tick thread held with the reward's added, under a new operation ID. The progress is
///     the one the tick thread
///     held, which the last checkpoint may not have reached yet.
/// </summary>
public sealed class QuestRewardCommit
{
    public QuestRewardCommit(
        Guid operationId,
        long characterId,
        string questDefinitionId,
        int progress,
        int count,
        int coins,
        int level,
        long experience,
        DateTime at,
        int jobLevel = 1,
        long jobExperience = 0)
    {
        if (count < 1 || coins < 0 || level < 1 || experience < 0 || jobLevel < 1 || jobExperience < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "The count and the level are at least 1, and the coins and the experience at least 0.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        QuestDefinitionId = questDefinitionId ?? throw new ArgumentNullException(nameof(questDefinitionId));
        Progress = progress;
        Count = count;
        Coins = coins;
        Level = level;
        Experience = experience;
        At = at;
        JobLevel = jobLevel;
        JobExperience = jobExperience;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public string QuestDefinitionId { get; }

    /// <summary>
    ///     The quest's progress as the tick thread held it.
    /// </summary>
    public int Progress { get; }

    /// <summary>
    ///     The kills the quest's objective needs.
    /// </summary>
    public int Count { get; }

    /// <summary>
    ///     The coins the reward pays.
    /// </summary>
    public int Coins { get; }

    /// <summary>
    ///     The level with the reward's experience added.
    /// </summary>
    public int Level { get; }

    /// <summary>
    ///     The experience toward the next level with the reward's experience added.
    /// </summary>
    public long Experience { get; }

    public DateTime At { get; }

    /// <summary>
    ///     The job level with the reward's job experience added.
    /// </summary>
    public int JobLevel { get; }

    /// <summary>
    ///     The job experience toward the next job level with the reward's added.
    /// </summary>
    public long JobExperience { get; }
}
}
