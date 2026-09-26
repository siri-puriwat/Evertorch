using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One quest of a character (Persistence §4): accepted, with its progress, or completed.
/// </summary>
internal sealed class CharacterQuestRow
{
    public const string ActiveState = "active";
    public const string CompletedState = "completed";

    public long CharacterId { get; set; }

    public string QuestDefinitionId { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    /// <summary>
    ///     How many of the objective's monsters the character has defeated.
    /// </summary>
    public int Progress { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int Version { get; set; }
}
}
