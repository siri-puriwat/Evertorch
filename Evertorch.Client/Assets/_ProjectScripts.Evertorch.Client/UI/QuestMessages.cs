using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The client's words for a quest (Prototype Content §2), composed from display names and the values on the wire:
///     the NPC window's lines, the status bar's part, and the chat's system lines. No text comes from content.
/// </summary>
public static class QuestMessages
{
    public static string QuestName(ClientContent? content, QuestDefinitionId quest)
    {
        return content != null && content.TryGetQuest(quest, out ClientQuest? found) && found != null
            ? found.DisplayName
            : quest.Value;
    }

    /// <summary>
    ///     "Defeat: Forest Crawler × 5".
    /// </summary>
    public static string Objective(NpcQuestOffer offer, ClientContent? content)
    {
        return $"Defeat: {MonsterName(content, offer.Monster)} × {offer.Count}";
    }

    /// <summary>
    ///     "Reward: 150 base experience, 150 job experience, 100 coins", each part left out when it is 0.
    /// </summary>
    public static string Reward(NpcQuestOffer offer)
    {
        return $"Reward: {Rewards(offer)}";
    }

    /// <summary>
    ///     The status bar's part for an active quest: "Forest Crawler 3/5", and "(ready)" once it reaches its count. The
    ///     quest's own name stands in for the monster while no offer of it has come this session.
    /// </summary>
    public static string Progress(QuestLogEntry entry, NpcQuestOffer? offer, ClientContent? content)
    {
        string subject = offer.HasValue
            ? MonsterName(content, offer.Value.Monster)
            : QuestName(content, entry.Quest);
        string counted = $"{subject} {entry.Progress}/{entry.Count}";
        return entry.Progress >= entry.Count ? $"{counted} (ready)" : counted;
    }

    /// <summary>
    ///     The chat's system line for one quest between two quest logs, or null when nothing a player would notice changed:
    ///     "Accepted Crawler Hunt.", "Crawler Hunt: Forest Crawler 3/5.", "Crawler Hunt is ready to turn in.", or
    ///     "Completed Crawler Hunt: 150 base experience, 150 job experience, 100 coins."
    /// </summary>
    public static string? Describe(
        QuestLogEntry? before,
        QuestLogEntry after,
        NpcQuestOffer? offer,
        ClientContent? content)
    {
        string name = QuestName(content, after.Quest);
        if (after.State == QuestState.Completed)
        {
            if (before?.State == QuestState.Completed)
            {
                return null;
            }

            return offer.HasValue ? $"Completed {name}: {Rewards(offer.Value)}." : $"Completed {name}.";
        }

        if (before == null)
        {
            return $"Accepted {name}.";
        }

        if (after.Progress == before.Value.Progress)
        {
            return null;
        }

        if (after.Progress >= after.Count)
        {
            return $"{name} is ready to turn in.";
        }

        string counted = $"{after.Progress}/{after.Count}";
        return offer.HasValue
            ? $"{name}: {MonsterName(content, offer.Value.Monster)} {counted}."
            : $"{name}: {counted}.";
    }

    private static string MonsterName(ClientContent? content, MonsterDefinitionId monster)
    {
        return content != null && content.TryGetMonster(monster, out ClientMonster? found) && found != null
            ? found.DisplayName
            : monster.Value;
    }

    private static string Rewards(NpcQuestOffer offer)
    {
        var parts = new List<string>(3);
        if (offer.BaseExperience > 0)
        {
            parts.Add($"{offer.BaseExperience.ToString(CultureInfo.InvariantCulture)} base experience");
        }

        if (offer.JobExperience > 0)
        {
            parts.Add($"{offer.JobExperience.ToString(CultureInfo.InvariantCulture)} job experience");
        }

        if (offer.Coins > 0)
        {
            parts.Add(TradeMessages.Coins(offer.Coins));
        }

        return string.Join(", ", parts);
    }
}
}
