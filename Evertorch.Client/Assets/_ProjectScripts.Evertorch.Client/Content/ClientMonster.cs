using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a monster definition: its name and the logical keys of its view, its icon, and the
///     projectile its ranged attacks and casts fly.
/// </summary>
public sealed class ClientMonster
{
    public ClientMonster(
        MonsterDefinitionId id,
        string displayName,
        string prefabKey,
        string iconKey,
        string projectileKey = "")
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PrefabKey = prefabKey ?? throw new ArgumentNullException(nameof(prefabKey));
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
        ProjectileKey = projectileKey ?? throw new ArgumentNullException(nameof(projectileKey));
    }

    public MonsterDefinitionId Id { get; }

    public string DisplayName { get; }

    public string PrefabKey { get; }

    public string IconKey { get; }

    /// <summary>Empty when the monster flies no projectile.</summary>
    public string ProjectileKey { get; }
}
}
