using System;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A monster's health bar: the HP ratio the server shares, drawn over the monster and turned to the camera.
/// </summary>
public sealed class HealthBar : MonoBehaviour
{
    private const float Width = 1f;
    private const float Height = 0.1f;

    // In front of the background, as seen from the camera the bar faces.
    private const float FillDepth = -0.01f;

    private Transform? m_fill;

    public float ShownRatio { get; private set; }

    public static HealthBar Create(Material background, Material fill)
    {
        if (background == null)
        {
            throw new ArgumentNullException(nameof(background));
        }

        if (fill == null)
        {
            throw new ArgumentNullException(nameof(fill));
        }

        var root = new GameObject("HealthBar");
        HealthBar bar = root.AddComponent<HealthBar>();
        CreateQuad("Background", root.transform, background, 0f);
        bar.m_fill = CreateQuad("Fill", root.transform, fill, FillDepth);
        return bar;
    }

    public void Show(Vector3 position, Camera? facing, int healthPermille)
    {
        transform.position = position;
        if (facing != null)
        {
            transform.rotation = facing.transform.rotation;
        }

        ShownRatio = Mathf.Clamp01(healthPermille / 1000f);
        if (m_fill != null)
        {
            m_fill.localScale = new Vector3(Width * ShownRatio, Height, 1f);
            m_fill.localPosition = new Vector3(-Width * (1f - ShownRatio) * 0.5f, 0f, FillDepth);
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private static Transform CreateQuad(string objectName, Transform parent, Material material, float depth)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = objectName;
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = new Vector3(0f, 0f, depth);
        quad.transform.localScale = new Vector3(Width, Height, 1f);
        return quad.transform;
    }
}
}
