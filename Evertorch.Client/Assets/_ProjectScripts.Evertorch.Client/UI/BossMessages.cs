using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of a boss's appearance and fall (Prototype Content §2), as grey lines in the chat log.
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

    private static string NameOf(ClientContent? content, MonsterDefinitionId monster)
    {
        return content != null && content.TryGetMonster(monster, out ClientMonster? known) && known != null
            ? known.DisplayName
            : monster.Value;
    }
}
}
