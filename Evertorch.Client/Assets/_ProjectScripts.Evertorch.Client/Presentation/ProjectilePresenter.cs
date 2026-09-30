using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     Flies a projectile for every attack and cast of a monster whose content names one, and for every cast of a skill
///     whose content names one, by any caster, the local player included (Gameplay Systems §8; Prototype Content §2),
///     from the caster's projectile origin to its target's arrival point, on the interpolated timeline, so that it lands
///     when the impact or the resolution is drawn; an attack leaves at its thrower's release marker when its clip has
///     one. The key's Addressables prefab flies when there is one, a plain sphere otherwise. Like
///     the combat presenter it only draws.
/// </summary>
public sealed class ProjectilePresenter : IDisposable
{
    private const float SphereSize = 0.25f;

    private static readonly Color SphereColor = new(1f, 0.95f, 0.55f);

    private readonly ClientWorld m_world;
    private readonly ClientContent m_content;
    private readonly double m_tickSeconds;
    private readonly Material m_sphereMaterial;
    private readonly Dictionary<string, GameObject?> m_prefabs = new(StringComparer.Ordinal);
    private readonly List<Flight> m_flights = new();

    public ProjectilePresenter(
        ClientWorld world,
        ClientContent content,
        EntityViewCatalog catalog,
        double tickSeconds,
        Material baseMaterial)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_content = content ?? throw new ArgumentNullException(nameof(content));
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (baseMaterial == null)
        {
            throw new ArgumentNullException(nameof(baseMaterial));
        }

        m_tickSeconds = tickSeconds;
        m_sphereMaterial = new Material(baseMaterial) { color = SphereColor };
        foreach (ClientMonster monster in content.Monsters)
        {
            RequestPrefab(catalog, monster.ProjectileKey);
        }

        foreach (ClientSkill skill in content.Skills)
        {
            RequestPrefab(catalog, skill.ProjectileKey);
        }

        m_world.AttackStartedReceived += OnAttackStarted;
        m_world.SkillCastStartedReceived += OnSkillCastStarted;
        m_world.EntityDiedReceived += OnEntityDied;
        m_world.RemoteDespawned += OnRemoteDespawned;
    }

    /// <summary>Projectiles waiting to be launched or in the air.</summary>
    public int Pending => m_flights.Count;

    /// <summary>Projectiles that have been drawn in the air, for the tests.</summary>
    public int Launched { get; private set; }

    public void Dispose()
    {
        m_world.AttackStartedReceived -= OnAttackStarted;
        m_world.SkillCastStartedReceived -= OnSkillCastStarted;
        m_world.EntityDiedReceived -= OnEntityDied;
        m_world.RemoteDespawned -= OnRemoteDespawned;
        foreach (Flight flight in m_flights)
        {
            flight.Destroy();
        }

        m_flights.Clear();
        Object.Destroy(m_sphereMaterial);
    }

    /// <summary>
    ///     Places every projectile in the air for this frame. Call after the views were placed.
    /// </summary>
    public void Present(EntityView? local, IReadOnlyDictionary<EntityId, EntityView> remotes)
    {
        double now = m_world.RemoteRenderTime;
        for (int index = m_flights.Count - 1; index >= 0; index--)
        {
            Flight flight = m_flights[index];
            if (now >= flight.Timing.ImpactSeconds)
            {
                flight.Destroy();
                m_flights.RemoveAt(index);
                continue;
            }

            EntityView? from = ViewOf(flight.Source, local, remotes);
            EntityView? to = ViewOf(flight.Target, local, remotes);
            ProjectileFlight timing = flight.IsAttack && from != null && from.TryGetAttackRelease(out double share)
                ? flight.Timing.Released(share)
                : flight.Timing;
            if (from == null || to == null || !timing.TryGetProgress(now, out float progress))
            {
                flight.Hide();
                continue;
            }

            if (flight.View == null)
            {
                flight.View = CreateView(flight.Key);
                Launched++;
            }

            flight.View.SetActive(true);
            flight.View.transform.position = Vector3.Lerp(from.ProjectileOrigin(), to.ProjectileArrival(), progress);
        }
    }

    private void OnAttackStarted(AttackStarted started)
    {
        if (TryGetProjectile(started.Attacker, out string key))
        {
            double start = started.StartTick * m_tickSeconds;
            m_flights.Add(
                new Flight(
                    key,
                    started.Attacker,
                    started.Target,
                    new ProjectileFlight(start, start + started.Timing.Impact.TotalSeconds),
                    true));
        }
    }

    /// <summary>
    ///     The projectile a cast throws: the skill's own, from any caster, or else its monster caster's (Gameplay Systems
    ///     §8). The combat presenter times the caster's release by it.
    /// </summary>
    public static bool TryGetCastProjectile(
        ClientWorld world,
        ClientContent content,
        EntityId caster,
        SkillDefinitionId skill,
        out string key)
    {
        key = content.TryGetSkill(skill, out ClientSkill? definition) && definition != null
            ? definition.ProjectileKey
            : string.Empty;
        return key.Length > 0 || TryGetMonsterProjectile(world, content, caster, out key);
    }

    private void OnSkillCastStarted(SkillCastStarted started)
    {
        if (started.Target != default
            && started.Target != started.Caster
            && TryGetCastProjectile(m_world, m_content, started.Caster, started.Skill, out string key))
        {
            double start = started.StartTick * m_tickSeconds;
            m_flights.Add(
                new Flight(
                    key,
                    started.Caster,
                    started.Target,
                    new ProjectileFlight(start, start + started.CastMs / 1000.0)));
        }
    }

    // A death ends what the dead entity threw and what was thrown at it, except what lands by the death's moment:
    // the blow that killed it still arrives.
    private void OnEntityDied(EntityDied died)
    {
        double at = died.ServerTick * m_tickSeconds;
        for (int index = m_flights.Count - 1; index >= 0; index--)
        {
            Flight flight = m_flights[index];
            if ((flight.Source == died.Entity || flight.Target == died.Entity) && flight.Timing.ImpactSeconds > at)
            {
                flight.Destroy();
                m_flights.RemoveAt(index);
            }
        }
    }

    private void OnRemoteDespawned(RemoteEntity remote)
    {
        for (int index = m_flights.Count - 1; index >= 0; index--)
        {
            Flight flight = m_flights[index];
            if (flight.Source == remote.Entity || flight.Target == remote.Entity)
            {
                flight.Destroy();
                m_flights.RemoveAt(index);
            }
        }
    }

    private bool TryGetProjectile(EntityId attacker, out string key)
    {
        return TryGetMonsterProjectile(m_world, m_content, attacker, out key);
    }

    // Only a monster the client draws flies a projectile, and only when its definition names one.
    private static bool TryGetMonsterProjectile(
        ClientWorld world,
        ClientContent content,
        EntityId attacker,
        out string key)
    {
        key = string.Empty;
        if (!world.Remotes.TryGetValue(attacker, out RemoteEntity? remote)
            || remote.Kind != EntityKind.Monster
            || !MonsterDefinitionId.TryCreate(remote.DefinitionId, out MonsterDefinitionId id)
            || !content.TryGetMonster(id, out ClientMonster? monster)
            || monster == null)
        {
            return false;
        }

        key = monster.ProjectileKey;
        return key.Length > 0;
    }

    private EntityView? ViewOf(
        EntityId entity,
        EntityView? local,
        IReadOnlyDictionary<EntityId, EntityView> remotes)
    {
        if (entity == m_world.LocalEntity)
        {
            return local;
        }

        return remotes.TryGetValue(entity, out EntityView? view) ? view : null;
    }

    private void RequestPrefab(EntityViewCatalog catalog, string key)
    {
        if (key.Length > 0 && !m_prefabs.ContainsKey(key))
        {
            m_prefabs.Add(key, null);
            catalog.Request(key, prefab => m_prefabs[key] = prefab);
        }
    }

    private GameObject CreateView(string key)
    {
        if (m_prefabs.TryGetValue(key, out GameObject? prefab) && prefab != null)
        {
            GameObject instance = Object.Instantiate(prefab);
            instance.name = "Projectile " + key;
            return instance;
        }

        // Without an entry, a plain sphere; its collider goes, so that no click ever lands on it.
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(sphere.GetComponent<Collider>());
        sphere.name = "Projectile " + key;
        sphere.transform.localScale = Vector3.one * SphereSize;
        sphere.GetComponent<MeshRenderer>().sharedMaterial = m_sphereMaterial;
        return sphere;
    }

    private sealed class Flight
    {
        public Flight(string key, EntityId source, EntityId target, ProjectileFlight timing, bool isAttack = false)
        {
            Key = key;
            Source = source;
            Target = target;
            Timing = timing;
            IsAttack = isAttack;
        }

        public string Key { get; }

        public EntityId Source { get; }

        public EntityId Target { get; }

        public ProjectileFlight Timing { get; }

        /// <summary>
        ///     A basic attack's, which leaves at its thrower's release marker when its clip has one.
        /// </summary>
        public bool IsAttack { get; }

        public GameObject? View { get; set; }

        public void Hide()
        {
            if (View != null)
            {
                View.SetActive(false);
            }
        }

        public void Destroy()
        {
            if (View != null)
            {
                Object.Destroy(View);
            }

            View = null;
        }
    }
}
}
