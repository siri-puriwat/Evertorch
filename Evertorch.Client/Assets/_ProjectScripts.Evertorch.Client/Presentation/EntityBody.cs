using System;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     What a body prefab tells its view (Prototype Content §2): where bars, numbers, and projectiles stand, the sphere a
///     click picks it by, and which material slots take the player tint. The art importer fills it from a delivery's
///     manifest; a graybox prefab carries today's fixed heights.
/// </summary>
public sealed class EntityBody : MonoBehaviour
{
    /// <summary>
    ///     The largest pick radius (Prototype Content §2.1): a larger sphere would take a click on the ground beside the
    ///     body, since the picker tries entities before the ground.
    /// </summary>
    public const float MaxPickRadius = 0.7f;

    [SerializeField]
    private Transform? m_overhead;

    [SerializeField]
    private Transform? m_projectile;

    [SerializeField]
    private Transform? m_pick;

    [SerializeField]
    private float m_pickRadius = MaxPickRadius;

    [SerializeField]
    private TrimSlot[] m_trimSlots = Array.Empty<TrimSlot>();

    public Transform? Overhead => m_overhead;

    /// <summary>
    ///     Where a projectile leaves the body; absent on a body that throws none.
    /// </summary>
    public Transform? Projectile => m_projectile;

    public Transform? Pick => m_pick;

    public float PickRadius => m_pickRadius;

    /// <summary>
    ///     The renderers' material slots the player tint colours; none means the whole body.
    /// </summary>
    public TrimSlot[] TrimSlots => m_trimSlots;

    public void Configure(
        Transform? overhead,
        Transform? projectile,
        Transform? pick,
        float pickRadius,
        TrimSlot[] trimSlots)
    {
        m_overhead = overhead;
        m_projectile = projectile;
        m_pick = pick;
        m_pickRadius = Mathf.Min(pickRadius, MaxPickRadius);
        m_trimSlots = trimSlots;
    }

    /// <summary>
    ///     One material slot of one renderer.
    /// </summary>
    [Serializable]
    public struct TrimSlot
    {
        [SerializeField]
        private Renderer? m_renderer;

        [SerializeField]
        private int m_materialIndex;

        public TrimSlot(Renderer renderer, int materialIndex)
        {
            m_renderer = renderer;
            m_materialIndex = materialIndex;
        }

        public Renderer? Renderer => m_renderer;

        public int MaterialIndex => m_materialIndex;
    }
}
}
