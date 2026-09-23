using System;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A ring under the target the server confirmed. It follows the target's drawn position and hides when there is
///     none.
/// </summary>
public sealed class TargetMarker : MonoBehaviour
{
    private static readonly Color RingColor = new(0.95f, 0.8f, 0.2f);

    private Material? m_material;
    private MeshRenderer? m_renderer;

    private void OnDestroy()
    {
        if (m_material != null)
        {
            Destroy(m_material);
        }
    }

    public static TargetMarker Create(Material baseMaterial)
    {
        if (baseMaterial == null)
        {
            throw new ArgumentNullException(nameof(baseMaterial));
        }

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "TargetMarker";
        DestroyImmediate(ring.GetComponent<Collider>());
        ring.transform.localScale = new Vector3(1.4f, 0.02f, 1.4f);
        TargetMarker marker = ring.AddComponent<TargetMarker>();
        marker.m_material = new Material(baseMaterial) { color = RingColor };
        marker.m_renderer = ring.GetComponent<MeshRenderer>();
        marker.m_renderer.sharedMaterial = marker.m_material;
        marker.m_renderer.enabled = false;
        return marker;
    }

    public void Show(WorldPosition position)
    {
        transform.position = new Vector3(position.X, position.Y + 0.02f, position.Z);
        if (m_renderer != null)
        {
            m_renderer.enabled = true;
        }
    }

    public void Hide()
    {
        if (m_renderer != null)
        {
            m_renderer.enabled = false;
        }
    }
}
}
