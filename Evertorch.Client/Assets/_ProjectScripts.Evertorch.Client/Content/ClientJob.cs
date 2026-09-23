using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a job definition: its name and the logical key of a character's view.
/// </summary>
public sealed class ClientJob
{
    public ClientJob(JobDefinitionId id, string displayName, string prefabKey)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PrefabKey = prefabKey ?? throw new ArgumentNullException(nameof(prefabKey));
    }

    public JobDefinitionId Id { get; }

    public string DisplayName { get; }

    public string PrefabKey { get; }
}
}
