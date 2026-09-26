using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One of a character's quests as stored (Persistence §4): accepted, with its progress, or completed. The quest's ID
///     is the stored text, which the current content may no longer define (Persistence §8).
/// </summary>
public sealed class StoredQuest
{
    public StoredQuest(string questDefinitionId, bool isCompleted, int progress)
    {
        QuestDefinitionId = questDefinitionId ?? throw new ArgumentNullException(nameof(questDefinitionId));
        IsCompleted = isCompleted;
        Progress = progress;
    }

    public string QuestDefinitionId { get; }

    public bool IsCompleted { get; }

    /// <summary>
    ///     How many of the objective's monsters the character has defeated since accepting it.
    /// </summary>
    public int Progress { get; }
}
}
