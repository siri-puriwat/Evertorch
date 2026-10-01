using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     A line said nearby shows over its speaker for 5 s (Prototype Content §2), the local player's over its own body;
///     a newer line replaces it. Rich text is off. It only draws.
/// </summary>
public sealed class ChatBubblePresenter : IDisposable
{
    public const float Seconds = 5f;

    // Over the floating numbers, which rise from 0.4 m over the body's overhead point.
    private const float Lift = 0.75f;
    private const float FontSize = 2f;
    private static readonly Vector2 Area = new(4f, 1.2f);

    private readonly Dictionary<EntityId, Bubble> m_bubbles = new();
    private readonly List<EntityId> m_expired = new();
    private readonly EntityId m_local;

    public ChatBubblePresenter(EntityId local)
    {
        m_local = local;
    }

    public void Dispose()
    {
        foreach (Bubble bubble in m_bubbles.Values)
        {
            Destroy(bubble);
        }

        m_bubbles.Clear();
    }

    public bool TryGetText(EntityId speaker, out string text)
    {
        bool isShown = m_bubbles.TryGetValue(speaker, out Bubble bubble) && bubble.Text != null;
        text = isShown ? bubble.Text!.text : string.Empty;
        return isShown;
    }

    public void Say(EntityId speaker, string text, float now)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        TextMeshPro label = m_bubbles.TryGetValue(speaker, out Bubble shown) && shown.Text != null
            ? shown.Text
            : Create();
        label.text = text;
        m_bubbles[speaker] = new Bubble(label, now + Seconds);
    }

    public void Present(
        EntityView? local,
        IReadOnlyDictionary<EntityId, EntityView> remotes,
        Camera? camera,
        float now)
    {
        if (remotes == null)
        {
            throw new ArgumentNullException(nameof(remotes));
        }

        m_expired.Clear();
        foreach (KeyValuePair<EntityId, Bubble> pair in m_bubbles)
        {
            EntityView? view = pair.Key == m_local ? local :
                remotes.TryGetValue(pair.Key, out EntityView? seen) ? seen : null;
            if (now >= pair.Value.ExpiresAt || view == null || pair.Value.Text == null)
            {
                m_expired.Add(pair.Key);
                continue;
            }

            Transform bubble = pair.Value.Text.transform;
            bubble.position = view.OverheadPoint(Lift);
            if (camera != null)
            {
                bubble.rotation = camera.transform.rotation;
            }
        }

        foreach (EntityId gone in m_expired)
        {
            Destroy(m_bubbles[gone]);
            m_bubbles.Remove(gone);
        }
    }

    private static TextMeshPro Create()
    {
        var root = new GameObject("ChatBubble");
        TextMeshPro text = root.AddComponent<TextMeshPro>();
        text.richText = false;
        text.fontSize = FontSize;
        text.alignment = TextAlignmentOptions.Bottom;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.rectTransform.sizeDelta = Area;
        text.text = string.Empty;
        return text;
    }

    private static void Destroy(Bubble bubble)
    {
        if (bubble.Text != null)
        {
            Object.Destroy(bubble.Text.gameObject);
        }
    }

    private readonly struct Bubble
    {
        public Bubble(TextMeshPro? text, float expiresAt)
        {
            Text = text;
            ExpiresAt = expiresAt;
        }

        public TextMeshPro? Text { get; }

        public float ExpiresAt { get; }
    }
}
}
