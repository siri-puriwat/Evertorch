using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Evertorch.Client
{
/// <summary>
///     Plays a rigged body's clips from the server's timing (Gameplay Systems §8). The combat presenter hands it a
///     <see cref="BodyCue" /> every frame; it asks <see cref="BodyClipChoice" /> for the clip and the time, sets them,
///     and evaluates the pose itself. Its graph runs by hand, so nothing advances it but the cue, and no animation
///     event exists to call back.
/// </summary>
[RequireComponent(typeof(Animator))]
public sealed class BodyAnimator : MonoBehaviour
{
    // A body cannot walk faster than this; a larger step between two frames is a teleport, a respawn, or a map
    // change, and must not show as a burst of running.
    private const float FastestStep = 12f;

    // How quickly the shown speed follows the measured one, in seconds.
    private const float SpeedSmoothing = 0.1f;

    [SerializeField]
    private BodyClips? m_clips;

    private readonly Dictionary<string, AnimationClipPlayable> m_playables = new();
    private readonly string[] m_wired = { string.Empty, string.Empty };
    private PlayableGraph m_graph;
    private AnimationMixerPlayable m_mixer;
    private string m_previous = string.Empty;
    private double m_previousTime;
    private double m_fadeStarted;
    private double m_fadeSeconds;
    private Vector3 m_lastPosition;
    private bool m_hasLastPosition;
    private float m_speed;
    private double m_travelled;

    public BodyClips? Clips => m_clips;

    /// <summary>
    ///     The clip shown last; empty before the first frame.
    /// </summary>
    public string CurrentClip { get; private set; } = string.Empty;

    /// <summary>
    ///     The time in <see cref="CurrentClip" />, in seconds.
    /// </summary>
    public double CurrentTime { get; private set; }

    /// <summary>
    ///     How much of the pose <see cref="CurrentClip" /> gives this frame; below 1 while it fades in.
    /// </summary>
    public float CurrentWeight { get; private set; }

    public bool HasClips => m_clips != null && m_clips.Clips.Count > 0;

    private void Start()
    {
        // The first pose is drawn at once, so no frame shows the model's bind pose; the first real cue then shows at
        // once too, rather than fading in from this stand-in idle.
        Animate(default, transform.position, 0.0, 0f);
        CurrentClip = string.Empty;
        m_previous = string.Empty;
    }

    private void OnDestroy()
    {
        if (m_graph.IsValid())
        {
            m_graph.Destroy();
        }
    }

    public void Configure(BodyClips clips)
    {
        m_clips = clips;
    }

    /// <summary>
    ///     Shows the body for this frame. <paramref name="root" /> is where the view stands, from which the walk's speed
    ///     is measured; <paramref name="clock" /> is real time in seconds.
    /// </summary>
    public void Animate(BodyCue cue, Vector3 root, double clock, float deltaSeconds)
    {
        if (m_clips == null || !EnsureGraph())
        {
            return;
        }

        MeasureWalk(root, deltaSeconds);
        cue.Speed = m_speed;
        cue.Travelled = m_travelled;
        cue.Clock = clock;
        if (!BodyClipChoice.TryChoose(cue, m_clips, out BodyClipPick pick))
        {
            return;
        }

        if (pick.Clip != CurrentClip)
        {
            if (CurrentClip.Length > 0)
            {
                m_previous = CurrentClip;
                m_previousTime = CurrentTime;
                m_fadeStarted = clock;
                m_fadeSeconds = pick.FadeSeconds;
            }

            CurrentClip = pick.Clip;
        }

        CurrentTime = pick.Time;
        double fade = m_fadeSeconds > 0.0 ? (clock - m_fadeStarted) / m_fadeSeconds : 1.0;
        if (fade >= 1.0 || m_previous == CurrentClip)
        {
            m_previous = string.Empty;
            fade = 1.0;
        }

        CurrentWeight = (float)fade;
        Show(0, CurrentClip, CurrentTime, (float)fade);
        Show(1, m_previous, m_previousTime, 1f - (float)fade);
        m_graph.Evaluate(0f);
    }

    private bool EnsureGraph()
    {
        if (m_graph.IsValid())
        {
            return true;
        }

        if (!TryGetComponent(out Animator animator))
        {
            return false;
        }

        animator.applyRootMotion = false;
        m_graph = PlayableGraph.Create($"{name} body");
        m_graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        m_mixer = AnimationMixerPlayable.Create(m_graph, 2);
        var output = AnimationPlayableOutput.Create(m_graph, "Body", animator);
        output.SetSourcePlayable(m_mixer);
        m_graph.Play();
        return true;
    }

    private void MeasureWalk(Vector3 root, float deltaSeconds)
    {
        Vector3 step = root - m_lastPosition;
        step.y = 0f;
        float distance = m_hasLastPosition ? step.magnitude : 0f;
        m_lastPosition = root;
        m_hasLastPosition = true;
        if (deltaSeconds <= 0f || distance > FastestStep * deltaSeconds)
        {
            m_speed = 0f;
            return;
        }

        m_travelled += distance;
        float blend = Mathf.Clamp01(deltaSeconds / SpeedSmoothing);
        m_speed = Mathf.Lerp(m_speed, distance / deltaSeconds, blend);
    }

    // A port is wired again only when its clip changes, since a change of the graph's wiring makes the next evaluation
    // bind the animation again; a clip feeds one port at a time, so it leaves the other port first.
    private void Show(int input, string clip, double time, float weight)
    {
        if (clip.Length == 0 || weight <= 0f || !TryGetPlayable(clip, out AnimationClipPlayable playable))
        {
            m_mixer.SetInputWeight(input, 0f);
            return;
        }

        if (m_wired[input] != clip)
        {
            int other = 1 - input;
            if (m_wired[other] == clip)
            {
                m_graph.Disconnect(m_mixer, other);
                m_mixer.SetInputWeight(other, 0f);
                m_wired[other] = string.Empty;
            }

            if (m_mixer.GetInput(input).IsValid())
            {
                m_graph.Disconnect(m_mixer, input);
            }

            m_graph.Connect(playable, 0, m_mixer, input);
            m_wired[input] = clip;
        }

        playable.SetTime(time);
        m_mixer.SetInputWeight(input, weight);
    }

    private bool TryGetPlayable(string clip, out AnimationClipPlayable playable)
    {
        if (m_playables.TryGetValue(clip, out playable))
        {
            return true;
        }

        if (m_clips == null || !m_clips.TryGet(clip, out BodyClips.Entry entry) || entry.Clip == null)
        {
            return false;
        }

        playable = AnimationClipPlayable.Create(m_graph, entry.Clip);
        playable.SetApplyFootIK(false);
        playable.SetApplyPlayableIK(false);
        m_playables.Add(clip, playable);
        return true;
    }
}
}
