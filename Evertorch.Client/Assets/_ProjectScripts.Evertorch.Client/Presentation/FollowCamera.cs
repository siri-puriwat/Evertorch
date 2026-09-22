using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A fixed-angle camera that keeps one transform in view.
/// </summary>
public sealed class FollowCamera : MonoBehaviour
{
    [SerializeField]
    private Vector3 m_offset = new(0f, 15f, -11f);

    private Transform? m_target;

    public float YawDegrees => transform.eulerAngles.y;

    private void LateUpdate()
    {
        if (m_target == null)
        {
            return;
        }

        transform.position = m_target.position + m_offset;
        transform.rotation = Quaternion.LookRotation(-m_offset, Vector3.up);
    }

    public void Follow(Transform? target)
    {
        m_target = target;
    }
}
}
