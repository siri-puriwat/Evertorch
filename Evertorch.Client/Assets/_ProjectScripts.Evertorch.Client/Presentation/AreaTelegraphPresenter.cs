using System;
using System.Collections.Generic;
using Evertorch.Protocol;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     Draws the telegraph of an area skill (Gameplay Systems §9; Prototype Content §2): from its cast's start, for its
///     cast time, a ring of the skill's radius at the feet of its caster as drawn, on the interpolated timeline. Only the
///     caster's death or despawn ends it early, since no message tells of an interrupted cast. Like the combat presenter
///     it only draws.
/// </summary>
public sealed class AreaTelegraphPresenter : IDisposable
{
    public const string ObjectName = "AreaTelegraph";

    private const float RingWidth = 0.15f;
    private const float RingLift = 0.03f;
    private const int RingSegments = 64;

    private static readonly Color RingColor = new(0.95f, 0.25f, 0.15f);

    private readonly ClientWorld m_world;
    private readonly ClientContent m_content;
    private readonly double m_tickSeconds;
    private readonly Material m_material;
    private readonly List<Telegraph> m_telegraphs = new();

    public AreaTelegraphPresenter(ClientWorld world, ClientContent content, double tickSeconds, Material overlay)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_content = content ?? throw new ArgumentNullException(nameof(content));
        if (overlay == null)
        {
            throw new ArgumentNullException(nameof(overlay));
        }

        m_tickSeconds = tickSeconds;
        m_material = new Material(overlay) { color = RingColor };
        m_world.SkillCastStartedReceived += OnSkillCastStarted;
        m_world.EntityDiedReceived += OnEntityDied;
        m_world.RemoteDespawned += OnRemoteDespawned;
    }

    /// <summary>
    ///     Telegraphs begun and not yet over, drawn or waiting for their start, for the tests.
    /// </summary>
    public int Pending => m_telegraphs.Count;

    public void Dispose()
    {
        m_world.SkillCastStartedReceived -= OnSkillCastStarted;
        m_world.EntityDiedReceived -= OnEntityDied;
        m_world.RemoteDespawned -= OnRemoteDespawned;
        foreach (Telegraph telegraph in m_telegraphs)
        {
            telegraph.Destroy();
        }

        m_telegraphs.Clear();
        Object.Destroy(m_material);
    }

    /// <summary>
    ///     Places every telegraph for this frame. Call after the views were placed.
    /// </summary>
    public void Present(EntityView? local, IReadOnlyDictionary<EntityId, EntityView> remotes)
    {
        double now = m_world.RemoteRenderTime;
        for (int index = m_telegraphs.Count - 1; index >= 0; index--)
        {
            Telegraph telegraph = m_telegraphs[index];
            if (now >= telegraph.EndSeconds)
            {
                telegraph.Destroy();
                m_telegraphs.RemoveAt(index);
                continue;
            }

            EntityView? caster = telegraph.Caster == m_world.LocalEntity
                ? local
                : remotes.TryGetValue(telegraph.Caster, out EntityView? view)
                    ? view
                    : null;
            if (caster == null || now < telegraph.StartSeconds)
            {
                telegraph.Hide();
                continue;
            }

            telegraph.Show(caster.transform.position, m_material);
        }
    }

    // A flat ring whose outer edge is the area's edge, wound to face up; it has no collider, so no click lands on it.
    private static GameObject CreateRing(float radius, Material material)
    {
        var vertices = new Vector3[RingSegments * 2];
        int[] triangles = new int[RingSegments * 6];
        float inner = Mathf.Max(0f, radius - RingWidth);
        for (int segment = 0; segment < RingSegments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / RingSegments;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[segment * 2] = direction * inner;
            vertices[segment * 2 + 1] = direction * radius;
            int next = (segment + 1) % RingSegments;
            int first = segment * 6;
            triangles[first] = segment * 2;
            triangles[first + 1] = next * 2 + 1;
            triangles[first + 2] = segment * 2 + 1;
            triangles[first + 3] = segment * 2;
            triangles[first + 4] = next * 2;
            triangles[first + 5] = next * 2 + 1;
        }

        var mesh = new Mesh { name = ObjectName, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var ring = new GameObject(ObjectName);
        ring.AddComponent<MeshFilter>().sharedMesh = mesh;
        ring.AddComponent<MeshRenderer>().sharedMaterial = material;
        return ring;
    }

    private void OnSkillCastStarted(SkillCastStarted started)
    {
        if (started.CastMs > 0
            && m_content.TryGetSkill(started.Skill, out ClientSkill? skill)
            && skill != null
            && skill.AreaRadius > 0f)
        {
            double start = started.StartTick * m_tickSeconds;
            m_telegraphs.Add(new Telegraph(started.Caster, skill.AreaRadius, start, start + started.CastMs / 1000.0));
        }
    }

    private void OnEntityDied(EntityDied died)
    {
        EndCastsOf(died.Entity);
    }

    private void OnRemoteDespawned(RemoteEntity remote)
    {
        EndCastsOf(remote.Entity);
    }

    private void EndCastsOf(EntityId caster)
    {
        for (int index = m_telegraphs.Count - 1; index >= 0; index--)
        {
            if (m_telegraphs[index].Caster == caster)
            {
                m_telegraphs[index].Destroy();
                m_telegraphs.RemoveAt(index);
            }
        }
    }

    private sealed class Telegraph
    {
        private GameObject? m_view;

        public Telegraph(EntityId caster, float radius, double startSeconds, double endSeconds)
        {
            Caster = caster;
            Radius = radius;
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
        }

        public EntityId Caster { get; }

        public float Radius { get; }

        public double StartSeconds { get; }

        public double EndSeconds { get; }

        public void Show(Vector3 feet, Material material)
        {
            m_view ??= CreateRing(Radius, material);
            m_view.SetActive(true);
            m_view.transform.position = new Vector3(feet.x, feet.y + RingLift, feet.z);
        }

        public void Hide()
        {
            if (m_view != null)
            {
                m_view.SetActive(false);
            }
        }

        public void Destroy()
        {
            if (m_view != null)
            {
                Object.Destroy(m_view.GetComponent<MeshFilter>().sharedMesh);
                Object.Destroy(m_view);
            }

            m_view = null;
        }
    }
}
}
