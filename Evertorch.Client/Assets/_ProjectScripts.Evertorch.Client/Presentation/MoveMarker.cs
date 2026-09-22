using System;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Shows where a click landed: steady while the walk it started is active, and briefly in another colour when the
///     click was refused.
/// </summary>
public sealed class MoveMarker : MonoBehaviour
{
    private const float RefusalSeconds = 0.6f;

    private static readonly Color AcceptedColor = new(0.3f, 0.9f, 0.4f);
    private static readonly Color RefusedColor = new(0.95f, 0.25f, 0.2f);

    private Material? m_material;
    private MeshRenderer? m_renderer;
    private float m_hideAt;
    private bool m_isRefusal;

    private void OnDestroy()
    {
        if (m_material != null)
        {
            Destroy(m_material);
        }
    }

    public static MoveMarker Create(Material baseMaterial)
    {
        if (baseMaterial == null)
        {
            throw new ArgumentNullException(nameof(baseMaterial));
        }

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "MoveMarker";
        Destroy(marker.GetComponent<Collider>());
        marker.transform.localScale = new Vector3(0.5f, 0.03f, 0.5f);
        MoveMarker view = marker.AddComponent<MoveMarker>();
        view.m_material = new Material(baseMaterial);
        view.m_renderer = marker.GetComponent<MeshRenderer>();
        view.m_renderer.sharedMaterial = view.m_material;
        view.m_renderer.enabled = false;
        return view;
    }

    public void ShowAccepted(WorldPosition point)
    {
        Show(point, AcceptedColor, false);
    }

    public void ShowRefused(WorldPosition point)
    {
        Show(point, RefusedColor, true);
        m_hideAt = Time.unscaledTime + RefusalSeconds;
    }

    /// <param name="hasActiveWalk">Whether the walk the accepted marker stands for is still going.</param>
    public void Refresh(bool hasActiveWalk)
    {
        if (m_renderer == null || !m_renderer.enabled)
        {
            return;
        }

        bool isExpired = m_isRefusal ? Time.unscaledTime >= m_hideAt : !hasActiveWalk;
        if (isExpired)
        {
            m_renderer.enabled = false;
        }
    }

    private void Show(WorldPosition point, Color color, bool isRefusal)
    {
        if (m_renderer == null || m_material == null)
        {
            return;
        }

        transform.position = new Vector3(point.X, point.Y + 0.05f, point.Z);
        m_material.color = color;
        m_isRefusal = isRefusal;
        m_renderer.enabled = true;
    }
}
}
