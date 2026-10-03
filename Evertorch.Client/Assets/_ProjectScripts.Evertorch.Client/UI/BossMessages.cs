using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of a boss's appearance and fall, and of its most valuable player's award (Prototype Content §2), as
///     grey lines in the chat log.
/// </summary>
public static class BossMessages
{
    /// <summary>
    ///     "The Slime Monarch has appeared.", "The Slime Monarch has fallen.", or, with its most valuable player, "The
    ///     Slime Monarch has fallen. MVP: Anna."
    /// </summary>
    public static string Describe(BossAnnouncement announcement, ClientContent? content)
    {
        string boss = NameOf(content, announcement.Monster);
        if (announcement.Kind == BossAnnouncementKind.Appeared)
        {
            return $"The {boss} has appeared.";
        }

        return announcement.Name.Length == 0
            ? $"The {boss} has fallen."
            : $"The {boss} has fallen. MVP: {announcement.Name}.";
    }

    /// <summary>
    ///     "You are the MVP: 3,000 experience and Monarch Mantle.", naming an amount above one ("5 Monarch Jelly") and
    ///     leaving out what the award did not give, with " Your bag was full; it lies at your feet." when it does.
    /// </summary>
    public static string Describe(MvpAwarded award, ClientContent? content)
    {
        var gains = new List<string>(2);
        if (award.MvpExperience > 0)
        {
            gains.Add($"{award.MvpExperience.ToString("N0", CultureInfo.InvariantCulture)} experience");
        }

        if (award.Item is ItemDefinitionId item)
        {
            string name = ItemNameOf(content, item);
            gains.Add(award.Amount > 1 ? $"{award.Amount.ToString(CultureInfo.InvariantCulture)} {name}" : name);
        }

        string text = gains.Count == 0 ? "You are the MVP." : $"You are the MVP: {string.Join(" and ", gains)}.";
        return award.Placed == PrizePlacement.Feet ? text + " Your bag was full; it lies at your feet." : text;
    }

    private static string ItemNameOf(ClientContent? content, ItemDefinitionId item)
    {
        return content != null && content.TryGetItem(item, out ClientItem? known) && known != null
            ? known.DisplayName
            : item.Value;
    }

    private static string NameOf(ClientContent? content, MonsterDefinitionId monster)
    {
        return content != null && content.TryGetMonster(monster, out ClientMonster? known) && known != null
            ? known.DisplayName
            : monster.Value;
    }
}
}
