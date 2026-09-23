using System;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     The drawn body of one entity. It is told where to stand every frame and decides nothing. The body is a prefab
///     loaded by its content key, or an explicit placeholder when the key does not resolve.
/// </summary>
public sealed class EntityView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Vector3 PlaceholderScale = new(0.6f, 1.6f, 0.6f);

    private Color? m_tint;

    public bool HasBody { get; private set; }

    public bool IsPlaceholder { get; private set; }

    public static EntityView Create(string objectName, string key, EntityViewCatalog catalog, Color? tint)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        var root = new GameObject(objectName);
        EntityView view = root.AddComponent<EntityView>();
        view.m_tint = tint;
        catalog.Request(key, prefab => view.AttachBody(prefab, catalog));
        return view;
    }

    public void SetPose(WorldPosition position, WorldDirection facing)
    {
        transform.position = new Vector3(position.X, position.Y, position.Z);
        if (facing != default)
        {
            transform.rotation = Quaternion.LookRotation(new Vector3(facing.X, 0f, facing.Z), Vector3.up);
        }
    }

    private void AttachBody(GameObject? prefab, EntityViewCatalog catalog)
    {
        // The load may finish after the entity has despawned.
        if (this == null || HasBody)
        {
            return;
        }

        GameObject body;
        if (prefab == null)
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.localPosition = new Vector3(0f, PlaceholderScale.y * 0.5f, 0f);
            body.transform.localScale = PlaceholderScale;
            body.GetComponent<MeshRenderer>().sharedMaterial = catalog.GetPlaceholderMaterial();
            IsPlaceholder = true;
        }
        else
        {
            body = Instantiate(prefab);
            ApplyTint(body);
        }

        body.name = "Body";
        body.transform.SetParent(transform, false);

        // Bodies must not catch the ground clicks meant for the map; entities are picked by their own test.
        foreach (Collider part in body.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(part);
        }

        HasBody = true;
    }

    private void ApplyTint(GameObject body)
    {
        if (m_tint == null)
        {
            return;
        }

        var block = new MaterialPropertyBlock();
        foreach (Renderer part in body.GetComponentsInChildren<Renderer>(true))
        {
            part.GetPropertyBlock(block);
            block.SetColor(BaseColorId, m_tint.Value);
            part.SetPropertyBlock(block);
        }
    }
}
}
