using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Plays a rigged body's clips from the server's timing (Gameplay Systems §8). It holds the rig's
///     <see cref="BodyClips" />; the art importer adds it beside the body's <see cref="Animator" />.
/// </summary>
public sealed class BodyAnimator : MonoBehaviour
{
    [SerializeField]
    private BodyClips? m_clips;

    public BodyClips? Clips => m_clips;

    public void Configure(BodyClips clips)
    {
        m_clips = clips;
    }
}
}
