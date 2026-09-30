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
    /// <summary>
    ///     Where bars and numbers start over a body that names no overhead point: a graybox body, a placeholder, or a
    ///     view whose body has not loaded (Prototype Content §2).
    /// </summary>
    public const float DefaultOverheadHeight = 1.2f;

    /// <summary>
    ///     Where a projectile leaves and lands on a body that names no point of its own.
    /// </summary>
    public const float DefaultProjectileHeight = 1f;

    // The procedural attack pose (Gameplay Systems §8): how far the lunge reaches and how much a hit squashes.
    private const float LungeDistance = 0.35f;
    private const float SquashWiden = 0.25f;

    private const float SquashFlatten = 0.35f;

    // The socket of the shared humanoid skeleton that a held weapon hangs from, at no offset (the art brief, §5).
    private const string WeaponSocket = "RightHand_Weapon";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Vector3 PlaceholderScale = new(0.6f, 1.6f, 0.6f);
    private static readonly Vector3 DeadScale = new(1.3f, 0.3f, 1.3f);
    private static readonly Vector3 DefaultProjectilePoint = new(0f, DefaultProjectileHeight, 0f);

    private Color? m_tint;

    private Transform? m_pose;
    private Transform? m_body;
    private BodyAnimator? m_animator;
    private EntityViewCatalog? m_catalog;
    private string m_heldKey = string.Empty;
    private GameObject? m_heldPrefab;
    private GameObject? m_held;
    private Vector3 m_projectileOrigin = DefaultProjectilePoint;
    private Vector3 m_projectileArrival = DefaultProjectilePoint;

    /// <summary>
    ///     The Addressables key the body was requested with; empty when the content did not know the entity.
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    public bool HasBody { get; private set; }

    public bool IsPlaceholder { get; private set; }

    /// <summary>
    ///     The height over the view's feet where the health bar sits; the cast bar and the numbers stack above it.
    /// </summary>
    public float OverheadHeight { get; private set; } = DefaultOverheadHeight;

    /// <summary>
    ///     The height of the centre of the sphere a click picks the body by.
    /// </summary>
    public float PickCenterHeight { get; private set; } = EntityPicker.PickHeight;

    public float PickRadius { get; private set; } = EntityPicker.PickRadius;

    /// <summary>
    ///     Whether the body plays clips rather than the procedural pose (Gameplay Systems §8).
    /// </summary>
    public bool HasClips => m_animator != null && m_animator.HasClips;

    public BodyAnimator? BodyAnimator => m_animator;

    /// <summary>
    ///     The attack clip its held weapon's grip names; a monster's rig has its own.
    /// </summary>
    public string AttackClip { get; set; } = BodyClipChoice.AttackUnarmed;

    public static EntityView Create(string objectName, string key, EntityViewCatalog catalog, Color? tint)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        var root = new GameObject(objectName);
        EntityView view = root.AddComponent<EntityView>();
        view.m_tint = tint;
        view.m_catalog = catalog;
        view.Key = key;
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
    ///     The point <paramref name="lift" /> metres over the body's overhead point.
    /// </summary>
    public Vector3 OverheadPoint(float lift)
    {
        return transform.position + Vector3.up * (OverheadHeight + lift);
    }

    /// <summary>
    ///     Where a projectile the body throws leaves it, turning with the body.
    /// </summary>
    public Vector3 ProjectileOrigin()
    {
        return transform.TransformPoint(m_projectileOrigin);
    }

    /// <summary>
    ///     Where a projectile thrown at the body lands, turning with the body.
    /// </summary>
    public Vector3 ProjectileArrival()
    {
        return transform.TransformPoint(m_projectileArrival);
    }

    /// <summary>
    ///     Poses the body for this frame: <paramref name="lunge" /> and <paramref name="squash" /> run from 0 to 1, and
    ///     a dead body lies flat. Only the drawing changes, and only the pose pivot above the body moves, since a rigged
    ///     body's own root belongs to its animation.
    /// </summary>
    public void SetCombatPose(float lunge, float squash, bool isDead)
    {
        // A body with clips shows its attack, its flinch, and its death through them, and never lies flattened.
        if (m_pose == null || HasClips)
        {
            return;
        }

        if (isDead)
        {
            m_pose.localPosition = Vector3.zero;
            m_pose.localScale = DeadScale;
            return;
        }

        float widen = 1f + SquashWiden * squash;
        m_pose.localPosition = Vector3.forward * (LungeDistance * lunge);
        m_pose.localScale = new Vector3(widen, 1f - SquashFlatten * squash, widen);
    }

    /// <summary>
    ///     Shows a rigged body's clips for this frame; a body without clips ignores it.
    /// </summary>
    public void Animate(BodyCue cue, double clock, float deltaSeconds)
    {
        if (m_animator != null)
        {
            m_animator.Animate(cue, transform.position, clock, deltaSeconds);
        }
    }

    /// <summary>
    ///     Draws the weapon the body holds (Prototype Content §2): the held model named by <paramref name="heldKey" /> at
    ///     the hand's socket, whose grip picks the attack clip; empty holds nothing. A body without the socket, such as
    ///     a graybox body, holds nothing either.
    /// </summary>
    public void Hold(string heldKey)
    {
        if (heldKey == m_heldKey)
        {
            return;
        }

        m_heldKey = heldKey;
        m_heldPrefab = null;
        if (m_held != null)
        {
            Destroy(m_held);
            m_held = null;
        }

        AttackClip = BodyClipChoice.AttackUnarmed;
        if (heldKey.Length > 0 && m_catalog != null)
        {
            m_catalog.Request(heldKey, prefab => TakeHeld(heldKey, prefab));
        }
    }

    // A load that finishes after the weapon changed, or after the view went, is dropped.
    private void TakeHeld(string heldKey, GameObject? prefab)
    {
        if (this == null || heldKey != m_heldKey || prefab == null)
        {
            return;
        }

        m_heldPrefab = prefab;
        ShowHeld();
    }

    private void ShowHeld()
    {
        if (m_heldPrefab == null || m_body == null || m_held != null)
        {
            return;
        }

        Transform? socket = FindPart(m_body, WeaponSocket);
        if (socket == null)
        {
            return;
        }

        GameObject held = Instantiate(m_heldPrefab, socket, false);
        held.name = "Held";
        held.transform.localPosition = Vector3.zero;
        held.transform.localRotation = Quaternion.identity;
        held.transform.localScale = Vector3.one;
        foreach (Collider part in held.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(part);
        }

        m_held = held;
        AttackClip = held.TryGetComponent(out HeldWeapon weapon) && weapon.Grip == WeaponGrip.Staff
            ? BodyClipChoice.AttackStaff
            : BodyClipChoice.AttackSword;
    }

    private static Transform? FindPart(Transform root, string name)
    {
        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
        {
            if (part.name == name)
            {
                return part;
            }
        }

        return null;
    }

    private void AttachBody(GameObject? prefab, EntityViewCatalog catalog)
    {
        // The load may finish after the entity has despawned.
        if (this == null || HasBody)
        {
            return;
        }

        m_pose = new GameObject("Pose").transform;
        m_pose.SetParent(transform, false);
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
        }

        body.name = "Body";
        body.transform.SetParent(m_pose, false);
        m_body = body.transform;

        // Bodies must not catch the ground clicks meant for the map; entities are picked by their own test.
        foreach (Collider part in body.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(part);
        }

        EntityBody? anchors = body.TryGetComponent(out EntityBody found) ? found : null;
        if (anchors != null)
        {
            ReadAnchors(anchors);
        }

        if (body.TryGetComponent(out BodyAnimator animator) && animator.HasClips)
        {
            m_animator = animator;
        }

        ShowHeld();

        if (!IsPlaceholder && m_tint != null)
        {
            ApplyTint(body, anchors, m_tint.Value);
        }

        HasBody = true;
    }

    // Read once, as offsets from the view, so that neither a squash nor a death moves a bar or a number.
    private void ReadAnchors(EntityBody anchors)
    {
        if (anchors.Overhead != null)
        {
            OverheadHeight = transform.InverseTransformPoint(anchors.Overhead.position).y;
        }

        if (anchors.Pick != null)
        {
            Vector3 pick = transform.InverseTransformPoint(anchors.Pick.position);
            PickCenterHeight = pick.y;
            PickRadius = anchors.PickRadius;
            m_projectileArrival = pick;
            m_projectileOrigin = pick;
        }

        if (anchors.Projectile != null)
        {
            m_projectileOrigin = transform.InverseTransformPoint(anchors.Projectile.position);
        }
    }

    /// <summary>
    ///     Colours a player's body with its identity tint: the material slots its <see cref="EntityBody" /> names as
    ///     Trim, or, when it names none, every renderer, as a graybox body is coloured (Prototype Content §2).
    /// </summary>
    public static void ApplyTint(GameObject body, EntityBody? anchors, Color tint)
    {
        var block = new MaterialPropertyBlock();
        if (anchors != null && anchors.TrimSlots.Length > 0)
        {
            foreach (EntityBody.TrimSlot slot in anchors.TrimSlots)
            {
                if (slot.Renderer == null)
                {
                    continue;
                }

                slot.Renderer.GetPropertyBlock(block, slot.MaterialIndex);
                block.SetColor(BaseColorId, tint);
                slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
            }

            return;
        }

        foreach (Renderer part in body.GetComponentsInChildren<Renderer>(true))
        {
            part.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            part.SetPropertyBlock(block);
        }
    }
}
}
