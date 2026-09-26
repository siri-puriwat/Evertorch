using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     A character's quests in the world (Gameplay Systems §2.2), ordered by quest ID: each accepted one active with
///     its progress, or completed. A quest without an entry is available. Tick thread only; checkpoints write the
///     active ones forward only, and only a turn-in's commit completes one (Persistence §5, §6).
/// </summary>
public sealed class CharacterQuests
{
    private readonly List<CharacterQuest> m_quests = new();

    public IReadOnlyList<CharacterQuest> Entries => m_quests;

    /// <summary>
    ///     The stored quests, whose IDs the content has already been checked to define.
    /// </summary>
    public static CharacterQuests FromStored(IReadOnlyList<StoredQuest> stored)
    {
        var quests = new CharacterQuests();
        foreach (StoredQuest quest in stored)
        {
            quests.Insert(new CharacterQuest(new QuestDefinitionId(quest.QuestDefinitionId), quest.Progress)
            {
                IsCompleted = quest.IsCompleted
            });
        }

        return quests;
    }

    public bool TryGet(QuestDefinitionId quest, out CharacterQuest? entry)
    {
        entry = m_quests.Find(candidate => candidate.Quest == quest);
        return entry != null;
    }

    /// <summary>
    ///     Adds <paramref name="quest" /> active with no progress; kills before now never count.
    /// </summary>
    public CharacterQuest Accept(QuestDefinitionId quest)
    {
        if (TryGet(quest, out CharacterQuest? _))
        {
            throw new InvalidOperationException($"The character already has {quest.Value}.");
        }

        var entry = new CharacterQuest(quest, 0);
        Insert(entry);
        return entry;
    }

    /// <summary>
    ///     The active quests as a checkpoint writes them.
    /// </summary>
    public IReadOnlyList<StoredQuest> ToCheckpoint()
    {
        var active = new List<StoredQuest>();
        foreach (CharacterQuest quest in m_quests)
        {
            if (!quest.IsCompleted)
            {
                active.Add(new StoredQuest(quest.Quest.Value, false, quest.Progress));
            }
        }

        return active;
    }

    private void Insert(CharacterQuest entry)
    {
        int index = 0;
        while (index < m_quests.Count
               && string.CompareOrdinal(m_quests[index].Quest.Value, entry.Quest.Value) < 0)
        {
            index++;
        }

        m_quests.Insert(index, entry);
    }
}
}
