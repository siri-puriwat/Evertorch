using System;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a monster definition: its name, its level, and whether it is a boss; the logical keys
///     of its view, its icon, and the projectile its ranged attacks and casts fly; and the size and colour its body is
///     drawn in (Prototype Content §2).
/// </summary>
public sealed class ClientMonster
{
    public ClientMonster(
        MonsterDefinitionId id,
        string displayName,
        string prefabKey,
        string iconKey,
        string projectileKey = "",
        int level = 1,
        float scale = 1f,
        Color? tint = null,
        bool isBoss = false)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PrefabKey = prefabKey ?? throw new ArgumentNullException(nameof(prefabKey));
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
        ProjectileKey = projectileKey ?? throw new ArgumentNullException(nameof(projectileKey));
        Level = level;
        Scale = scale;
        Tint = tint;
        IsBoss = isBoss;
    }

    public MonsterDefinitionId Id { get; }

    public string DisplayName { get; }

    public string PrefabKey { get; }

    public string IconKey { get; }

    /// <summary>Empty when the monster flies no projectile.</summary>
    public string ProjectileKey { get; }

    public int Level { get; }

    /// <summary>How large its body is drawn against its model's own size; 1 when the package names none.</summary>
    public float Scale { get; }

    /// <summary>The colour its whole body is drawn in, or null for the model's own colours.</summary>
    public Color? Tint { get; }

    public bool IsBoss { get; }
}
}
