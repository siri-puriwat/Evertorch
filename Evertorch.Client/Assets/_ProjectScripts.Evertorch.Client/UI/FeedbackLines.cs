using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Short-lived lines over the lower middle of the screen (Prototype Content §2): a refused command in plain words,
///     and what the player picked up.
/// </summary>
public sealed class FeedbackLines : MonoBehaviour
{
    public const int MaxLines = 3;
    public const float LineSeconds = 4f;

    // Over the other in-world panels, under the login panel.
    private const int SortingOrder = 7;
    private const float Width = 640f;
    private const float BottomOffset = 400f;
    private const int Padding = 10;

    private static readonly UiBuilder Ui = new(24f, 32f, 0f, 4f);

    private readonly List<Line> m_lines = new();
    private readonly StringBuilder m_text = new();
    private GameClient? m_client;
    private ClientWorld? m_watched;
    private GameObject? m_panel;
    private TMP_Text? m_label;

    public int TextChanges { get; private set; }

    public string Text => m_label != null ? m_label.text : string.Empty;

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world != m_watched)
        {
            Watch(world);
        }

        int count = m_lines.Count;
        m_lines.RemoveAll(line => line.ExpiresAt <= Time.unscaledTime);
        if (m_lines.Count != count)
        {
            Rewrite();
        }
    }

    private void OnDestroy()
    {
        Watch(null);
    }

    public static FeedbackLines Create(GameClient client)
    {
        var root = new GameObject("FeedbackLines", typeof(RectTransform));
        root.SetActive(false);
        FeedbackLines lines = root.AddComponent<FeedbackLines>();
        lines.Build(client);
        root.SetActive(true);
        return lines;
    }

    public void Add(string line)
    {
        if (m_lines.Count == MaxLines)
        {
            m_lines.RemoveAt(0);
        }

        m_lines.Add(new Line(line, Time.unscaledTime + LineSeconds));
        Rewrite();
    }

    // A new world is a new entry; lines about the last one no longer apply.
    private void Watch(ClientWorld? world)
    {
        if (m_watched != null)
        {
            m_watched.CommandRejectedReceived -= OnRejected;
            m_watched.ItemPickedUpReceived -= OnPickedUp;
        }

        m_watched = world;
        if (world != null)
        {
            world.CommandRejectedReceived += OnRejected;
            world.ItemPickedUpReceived += OnPickedUp;
        }

        if (m_lines.Count > 0)
        {
            m_lines.Clear();
            Rewrite();
        }
    }

    private void OnRejected(CommandRejected rejected)
    {
        Add(RejectionMessages.Describe(rejected.Reason));
    }

    private void OnPickedUp(ItemPickedUp pickedUp)
    {
        if (m_watched != null && pickedUp.Recipient == m_watched.LocalEntity)
        {
            Add($"Picked up {ItemName(m_client != null ? m_client.Content : null, pickedUp.Item)} x {pickedUp.Amount}");
        }
    }

    private static string ItemName(ClientContent? content, ItemDefinitionId item)
    {
        return content != null && content.TryGetItem(item, out ClientItem? found) && found != null
            ? found.DisplayName
            : item.Value;
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel = Ui.CreatePanel(
            transform,
            new Vector2(0.5f, 0f),
            new Vector2(0f, BottomOffset),
            Width,
            Padding);
        m_panel = panel.gameObject;
        m_label = Ui.CreateLabel("Lines", panel);
        m_label.alignment = TextAlignmentOptions.Center;
        m_panel.SetActive(false);
    }

    private void Rewrite()
    {
        m_text.Clear();
        foreach (Line line in m_lines)
        {
            if (m_text.Length > 0)
            {
                m_text.Append('\n');
            }

            m_text.Append(line.Text);
        }

        m_label!.text = m_text.ToString();
        TextChanges++;
        UiBuilder.SetActive(m_panel!, m_lines.Count > 0);
    }

    private readonly struct Line
    {
        public Line(string text, float expiresAt)
        {
            Text = text;
            ExpiresAt = expiresAt;
        }

        public string Text { get; }

        public float ExpiresAt { get; }
    }
}
}
