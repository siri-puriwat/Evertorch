using System;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A cast's progress over its caster (Prototype Content §2), turned to the camera. It only shows time passing; the
///     server decides whether the cast resolves.
/// </summary>
public sealed class CastBar : MonoBehaviour
{
    private const float Width = 1f;
    private const float Height = 0.08f;

    // In front of the background, as seen from the camera the bar faces.
    private const float FillDepth = -0.01f;

    private Transform? m_fill;

    public float ShownProgress { get; private set; }

    public bool IsShown => gameObject.activeSelf;

    public static CastBar Create(Material background, Material fill)
    {
        if (background == null)
        {
            throw new ArgumentNullException(nameof(background));
        }

        if (fill == null)
        {
            throw new ArgumentNullException(nameof(fill));
        }

        var root = new GameObject("CastBar");
        CastBar bar = root.AddComponent<CastBar>();
        CreateQuad("Background", root.transform, background, 0f);
        bar.m_fill = CreateQuad("Fill", root.transform, fill, FillDepth);
        return bar;
    }

    public void Show(Vector3 position, Camera? facing, float progress)
    {
        transform.position = position;
        if (facing != null)
        {
            transform.rotation = facing.transform.rotation;
        }

        ShownProgress = Mathf.Clamp01(progress);
        if (m_fill != null)
        {
            m_fill.localScale = new Vector3(Width * ShownProgress, Height, 1f);
            m_fill.localPosition = new Vector3(-Width * (1f - ShownProgress) * 0.5f, 0f, FillDepth);
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
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
