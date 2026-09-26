using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a quest: its name. Its objective and reward arrive on the wire (Content Pipeline §5).
/// </summary>
public sealed class ClientQuest
{
    public ClientQuest(QuestDefinitionId id, string displayName)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    public QuestDefinitionId Id { get; }

    public string DisplayName { get; }
}
}
