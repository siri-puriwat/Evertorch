using System;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
/// A placeholder body for one entity. It is told where to stand every frame and decides nothing.
/// </summary>
public sealed class EntityView : MonoBehaviour
{
    private const float BodyHeight = 1.6f;

    private Material? m_material;

    private void OnDestroy()
    {
        if (m_material != null)
        {
            Destroy(m_material);
        }
    }

    public static EntityView Create(string objectName, float radius, Material baseMaterial, Color color)
    {
        if (baseMaterial == null)
        {
            throw new ArgumentNullException(nameof(baseMaterial));
        }

        GameObject root = new GameObject(objectName);
        EntityView view = root.AddComponent<EntityView>();
        view.m_material = new Material(baseMaterial) { color = color };

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Attach(body, root.transform, view.m_material);
        body.transform.localPosition = new Vector3(0f, BodyHeight * 0.5f, 0f);
        body.transform.localScale = new Vector3(radius * 2f, BodyHeight * 0.5f, radius * 2f);

        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Attach(nose, root.transform, view.m_material);
        nose.transform.localPosition = new Vector3(0f, BodyHeight * 0.75f, radius);
        nose.transform.localScale = new Vector3(radius * 0.5f, radius * 0.5f, radius);
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

    private static void Attach(GameObject part, Transform parent, Material material)
    {
        // Bodies must not catch the ground clicks meant for the map.
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}
}
