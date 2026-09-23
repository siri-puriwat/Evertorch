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
    // The procedural attack pose (Gameplay Systems §8): how far the lunge reaches and how much a hit squashes.
    private const float LungeDistance = 0.35f;
    private const float SquashWiden = 0.25f;
    private const float SquashFlatten = 0.35f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Vector3 PlaceholderScale = new(0.6f, 1.6f, 0.6f);
    private static readonly Vector3 DeadScale = new(1.3f, 0.3f, 1.3f);

    private Color? m_tint;
    private Transform? m_body;
    private Vector3 m_bodyPosition;
    private Vector3 m_bodyScale;

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

    /// <summary>
    ///     Poses the body for this frame: <paramref name="lunge" /> and <paramref name="squash" /> run from 0 to 1, and
    ///     a dead body lies flat. Only the drawing changes.
    /// </summary>
    public void SetCombatPose(float lunge, float squash, bool isDead)
    {
        if (m_body == null)
        {
            return;
        }

        if (isDead)
        {
            m_body.localPosition = Vector3.Scale(m_bodyPosition, new Vector3(1f, DeadScale.y, 1f));
            m_body.localScale = Vector3.Scale(m_bodyScale, DeadScale);
            return;
        }

        float widen = 1f + SquashWiden * squash;
        m_body.localPosition = m_bodyPosition + Vector3.forward * (LungeDistance * lunge);
        m_body.localScale = Vector3.Scale(m_bodyScale, new Vector3(widen, 1f - SquashFlatten * squash, widen));
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
        m_body = body.transform;
        m_bodyPosition = m_body.localPosition;
        m_bodyScale = m_body.localScale;

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
