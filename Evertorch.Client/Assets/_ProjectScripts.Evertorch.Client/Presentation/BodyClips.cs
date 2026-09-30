using System;
using System.Collections.Generic;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     One rig's clips with their markers (Gameplay Systems §8): each clip's frame count at 30 a second, whether it
///     loops, its markers as frame numbers, and the speed its movement cycle was made for. The art importer writes it
///     from a delivery's manifest, and it lives beside the clips.
/// </summary>
public sealed class BodyClips : ScriptableObject
{
    public const int FramesPerSecond = 30;

    [SerializeField]
    private string m_rig = string.Empty;

    [SerializeField]
    private Entry[] m_clips = Array.Empty<Entry>();

    public string Rig => m_rig;

    public IReadOnlyList<Entry> Clips => m_clips;

    public void Configure(string rig, Entry[] clips)
    {
        m_rig = rig;
        m_clips = clips;
    }

    public bool TryGet(string name, out Entry entry)
    {
        foreach (Entry candidate in m_clips)
        {
            if (candidate.Name == name && candidate.Clip != null)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>
    ///     A named frame of a clip, such as <c>impact</c>.
    /// </summary>
    [Serializable]
    public struct Marker
    {
        [SerializeField]
        private string m_name;

        [SerializeField]
        private int m_frame;

        public Marker(string name, int frame)
        {
            m_name = name;
            m_frame = frame;
        }

        public string Name => m_name ?? string.Empty;

        public int Frame => m_frame;
    }

    [Serializable]
    public struct Entry
    {
        [SerializeField]
        private string m_name;

        [SerializeField]
        private AnimationClip? m_clip;

        [SerializeField]
        private int m_frames;

        [SerializeField]
        private bool m_loop;

        [SerializeField]
        private Marker[] m_markers;

        [SerializeField]
        private float m_calibrationSpeed;

        public Entry(string name, AnimationClip clip, int frames, bool loop, Marker[] markers, float calibrationSpeed)
        {
            m_name = name;
            m_clip = clip;
            m_frames = frames;
            m_loop = loop;
            m_markers = markers;
            m_calibrationSpeed = calibrationSpeed;
        }

        public string Name => m_name ?? string.Empty;

        public AnimationClip? Clip => m_clip;

        public int Frames => m_frames;

        public bool Loop => m_loop;

        public IReadOnlyList<Marker> Markers => m_markers ?? Array.Empty<Marker>();

        /// <summary>
        ///     The speed in metres a second a movement cycle was made for; 0 for any other clip.
        /// </summary>
        public float CalibrationSpeed => m_calibrationSpeed;

        public double Seconds => (double)m_frames / FramesPerSecond;

        public bool TryGetMarker(string name, out int frame)
        {
            foreach (Marker marker in Markers)
            {
                if (marker.Name == name)
                {
                    frame = marker.Frame;
                    return true;
                }
            }

            frame = 0;
            return false;
        }
    }
}
}
