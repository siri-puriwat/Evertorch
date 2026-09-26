using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     One quest a character has accepted: its progress toward the objective's count, and whether its turn-in committed.
/// </summary>
public sealed class CharacterQuest
{
    public CharacterQuest(QuestDefinitionId quest, int progress)
    {
        Quest = quest;
        Progress = progress;
    }

    public QuestDefinitionId Quest { get; }

    /// <summary>
    ///     The kills counted since the quest was accepted, never past the count.
    /// </summary>
    public int Progress { get; set; }

    public bool IsCompleted { get; set; }
}
}
