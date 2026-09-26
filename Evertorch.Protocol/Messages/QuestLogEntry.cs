using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One of the receiver's quests (Network Protocol §9): its state, how many of its objective's monsters the
///     character has defeated, and how many the objective needs.
/// </summary>
public readonly struct QuestLogEntry
{
    public QuestLogEntry(QuestDefinitionId quest, QuestState state, ushort progress, ushort count)
    {
        Quest = quest;
        State = state;
        Progress = progress;
        Count = count;
    }

    public QuestDefinitionId Quest { get; }

    public QuestState State { get; }

    /// <summary>
    ///     The kills counted since the quest was accepted; the count itself once it is completed.
    /// </summary>
    public ushort Progress { get; }

    public ushort Count { get; }
}
}
