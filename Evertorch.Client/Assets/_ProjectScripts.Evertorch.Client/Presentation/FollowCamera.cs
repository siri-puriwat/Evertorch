using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Keeps one transform in view from where <see cref="OrbitCameraState" /> says, pulled in when the map stands
///     between the player and the camera (Prototype Content §3).
/// </summary>
public sealed class FollowCamera : MonoBehaviour
{
    // The probe starts above the head: the map is one mesh collider, and a cast that starts inside it would miss it.
    private const float ProbeHeight = 2f;
    private const float ProbeRadius = 0.2f;
    private const float WallMargin = 0.2f;
    private const float NearestDistance = 0.25f;
    private const float EaseOutSeconds = 0.25f;

    private readonly RaycastHit[] m_hits = new RaycastHit[8];
    private Transform? m_target;
    private OrbitCameraState? m_state;
    private Collider? m_obstacles;
    private float m_shownDistance = float.PositiveInfinity;

    public float YawDegrees => transform.eulerAngles.y;

    private void LateUpdate()
    {
        if (m_target == null || m_state == null)
        {
            return;
        }

        m_state.GetOffset(out float offsetX, out float offsetY, out float offsetZ);
        Vector3 feet = m_target.position;
        Vector3 wanted = feet + new Vector3(offsetX, offsetY, offsetZ);
        Vector3 probe = feet + Vector3.up * ProbeHeight;
        Vector3 toCamera = wanted - probe;
        float full = toCamera.magnitude;
        float allowed = full > 0f ? AllowedDistance(probe, toCamera / full, full) : full;

        // In at once, so a wall never hides the player; out over a quarter second, so a corner passing by does not
        // jolt the view.
        m_shownDistance = allowed < m_shownDistance
            ? allowed
            : Mathf.MoveTowards(m_shownDistance, allowed, full / EaseOutSeconds * Time.unscaledDeltaTime);
        Vector3 position = m_shownDistance >= full ? wanted : probe + toCamera / full * m_shownDistance;
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(feet - position, Vector3.up);
    }

    /// <summary>
    ///     Follows <paramref name="target" /> from where <paramref name="state" /> says; <paramref name="obstacles" />,
    ///     the map's collider, pulls the camera in. Null stops following.
    /// </summary>
    public void Follow(Transform? target, OrbitCameraState? state, Collider? obstacles)
    {
        m_target = target;
        m_state = state;
        m_obstacles = obstacles;
        m_shownDistance = float.PositiveInfinity;
    }

    private float AllowedDistance(Vector3 probe, Vector3 direction, float full)
    {
        if (m_obstacles == null)
        {
            return full;
        }

        float allowed = full;
        int count = Physics.SphereCastNonAlloc(
            probe,
            ProbeRadius,
            direction,
            m_hits,
            full,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            if (m_hits[index].collider == m_obstacles)
            {
                allowed = Mathf.Min(allowed, Mathf.Max(NearestDistance, m_hits[index].distance - WallMargin));
            }
        }

        return allowed;
    }
}
}
