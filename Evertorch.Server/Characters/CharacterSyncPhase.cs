using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Sends each owner the state of its own character that no other message carries (Network Protocol §9): the skill
///     list, with what is left of each cooldown, after the inventory in every baseline and whenever one of its casts
///     resolves; then its status effects, with what is left of each, in every baseline and whenever one starts, is
///     renewed, or ends; and last its quests, in every baseline and whenever one is accepted, advances, or is
///     completed; and its <see cref="CharacterSheet" /> in the tick anything on it changes. Registered after
///     <see cref="InventorySyncPhase" /> in the same phase.
/// </summary>
public sealed class CharacterSyncPhase : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly SessionRegistry m_sessions;
    private readonly ServerContent m_content;
    private readonly MessageSender m_sender;
    private readonly CharacterBuilds m_builds;
    private readonly int m_tickRate;
    private readonly List<SkillListEntry> m_entries = new();
    private readonly List<StatusEffectEntry> m_effects = new();
    private readonly List<QuestLogEntry> m_quests = new();

    public CharacterSyncPhase(
        SessionRegistry sessions,
        ServerContent content,
        MessageSender sender,
        CharacterBuilds builds,
        IOptions<SimulationOptions> simulation)
    {
        m_sessions = sessions;
        m_content = content;
        m_sender = sender;
        m_builds = builds;
        m_tickRate = simulation.Value.TickRate;
    }

    public TickPhase Phase => TickPhase.FinalizeWorld;

    public void Execute(in TickContext context)
    {
        long now = (long)(context.Tick - 1) * MillisecondsPerSecond / m_tickRate;
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State != SessionState.InWorld || session.Player == null)
            {
                continue;
            }

            if (session.NeedsSkillList)
            {
                session.NeedsSkillList = false;
                m_sender.Send(session.Connection, CreateSkillList(session.Player, now));
            }

            if (session.NeedsStatusEffects)
            {
                session.NeedsStatusEffects = false;
                m_sender.Send(session.Connection, CreateStatusEffects(session.Player, now));
            }

            if (session.NeedsQuestLog && session.Character != null)
            {
                session.NeedsQuestLog = false;
                m_sender.Send(session.Connection, CreateQuestLog(session.Character.Quests));
            }

            // Anything that moves the build, the points, or a derived statistic changes the sheet, so the owner hears it
            // in the tick it changed.
            CharacterSheet sheet = m_builds.SheetOf(session.Player);
            if (!sheet.Equals(session.LastSheet))
            {
                session.LastSheet = sheet;
                m_sender.Send(session.Connection, sheet);
            }
        }
    }

    // A completed quest shows its count as its progress.
    private QuestLog CreateQuestLog(CharacterQuests quests)
    {
        m_quests.Clear();
        foreach (CharacterQuest quest in quests.Entries)
        {
            int count = m_content.Quests[quest.Quest].Count;
            m_quests.Add(
                new QuestLogEntry(
                    quest.Quest,
                    quest.IsCompleted ? QuestState.Completed : QuestState.Active,
                    (ushort)(quest.IsCompleted ? count : Math.Min(quest.Progress, count)),
                    (ushort)count));
        }

        return new QuestLog(m_quests.ToArray());
    }

    private StatusEffects CreateStatusEffects(PlayerEntity player, long now)
    {
        m_effects.Clear();
        foreach (ActiveStatusEffect effect in player.StatusEffects)
        {
            m_effects.Add(new StatusEffectEntry(effect.Status, (uint)Math.Max(0, effect.EndMs - now)));
        }

        return new StatusEffects(m_effects.ToArray());
    }

    private static int IndexOf(IReadOnlyList<SkillDefinitionId> tree, SkillDefinitionId skill)
    {
        for (int index = 0; index < tree.Count; index++)
        {
            if (tree[index] == skill)
            {
                return index;
            }
        }

        return -1;
    }

    private SkillList CreateSkillList(PlayerEntity player, long now)
    {
        m_entries.Clear();
        // The job's whole tree in its order, a first job's base tree first, each skill at its learned level's values, or
        // level 1's while not learned (Gameplay Systems §9).
        IReadOnlyList<SkillDefinitionId> tree = m_content.Jobs[player.Job].Tree;
        foreach (SkillDefinitionId id in tree)
        {
            player.Skills.TryGetValue(id, out int level);
            SkillDefinition skill = m_content.Skills[id];
            SkillLevel values = skill.ValuesAt(Math.Max(1, level));
            long end = player.Combat.CooldownEndMs(id);
            long remaining = end == long.MinValue ? 0 : Math.Max(0, end - now);
            SkillRequirement? requires = skill.Requires;
            int prerequisite = requires == null ? -1 : IndexOf(tree, requires.Skill);
            m_entries.Add(
                new SkillListEntry(
                    id,
                    (float)skill.Range,
                    (uint)values.SpCost,
                    (uint)values.CooldownMs,
                    (uint)values.AfterCastDelayMs,
                    (uint)Math.Min(remaining, values.CooldownMs),
                    (byte)level,
                    (byte)skill.MaxLevel,
                    prerequisite < 0 ? SkillListEntry.NoPrerequisite : (byte)prerequisite,
                    prerequisite < 0 ? (byte)0 : (byte)requires!.Level));
        }

        return new SkillList(m_entries.ToArray());
    }
}
}
