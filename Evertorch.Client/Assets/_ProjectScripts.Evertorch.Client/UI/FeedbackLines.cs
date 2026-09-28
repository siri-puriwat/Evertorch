using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Short-lived lines over the lower middle of the screen, clear of the skill bar (Prototype Content §2): a refused
///     command in plain words, what the player picked up, bought, or sold, a quest accepted, advanced, ready, or
///     completed, and a level-up.
/// </summary>
public sealed class FeedbackLines : MonoBehaviour
{
    public const int MaxLines = 3;
    public const float LineSeconds = 4f;

    /// <summary>
    ///     The panel's width in canvas units: narrow enough to pass between the stick and the touch buttons.
    /// </summary>
    public const float Width = 600f;

    // Over the other in-world panels, under the login panel.
    private const int SortingOrder = 7;
    private const int Padding = 10;
    private const float HeightShare = 0.2f;
    private const float BarClearance = 16f;

    private static readonly UiBuilder Ui = new(24f, 32f, 0f, 4f);

    private readonly List<Line> m_lines = new();
    private readonly StringBuilder m_text = new();
    private GameClient? m_client;
    private ClientWorld? m_watched;
    private IReadOnlyList<QuestLogEntry>? m_lastQuests;
    private GameObject? m_panel;
    private RectTransform? m_panelRect;
    private TMP_Text? m_label;
    private float m_placedForHeight = -1f;

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

        // The canvas height follows the screen's shape, so a rotated device or a resized window moves the lines.
        float canvasHeight = ((RectTransform)transform).rect.height;
        if (canvasHeight != m_placedForHeight)
        {
            m_placedForHeight = canvasHeight;
            m_panelRect!.anchoredPosition = new Vector2(0f, BottomFor(canvasHeight));
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

    /// <summary>
    ///     How high the lines' bottom edge sits, in canvas units (owner decision 10): about a fifth of the height,
    ///     raised to clear the skill bar.
    /// </summary>
    public static float BottomFor(float canvasHeight)
    {
        return Math.Max(HeightShare * canvasHeight, SkillBar.Top + BarClearance);
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
            m_watched.LeveledUp -= OnLeveledUp;
            m_watched.SheetChanged -= OnSheetChanged;
            m_watched.Inventory.ChangeApplied -= OnChangeApplied;
            m_watched.QuestsChanged -= OnQuestsChanged;
        }

        m_watched = world;

        // A world's first quest log is its baseline, which is no news; it may have come before this frame.
        m_lastQuests = world != null && world.QuestLogsReceived > 0 ? world.Quests : null;
        if (world != null)
        {
            world.CommandRejectedReceived += OnRejected;
            world.ItemPickedUpReceived += OnPickedUp;
            world.LeveledUp += OnLeveledUp;
            world.SheetChanged += OnSheetChanged;
            world.Inventory.ChangeApplied += OnChangeApplied;
            world.QuestsChanged += OnQuestsChanged;
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
            ClientContent? content = m_client != null ? m_client.Content : null;
            Add($"Picked up {TradeMessages.ItemName(content, pickedUp.Item)} x {pickedUp.Amount}");
        }
    }

    private void OnLeveledUp()
    {
        Add("Level up");
    }

    // A world's first sheet is its baseline, which is no news.
    private void OnSheetChanged(CharacterSheet? before)
    {
        CharacterSheet? after = m_watched?.Sheet;
        if (before != null && after != null && after.JobLevel > before.JobLevel)
        {
            Add($"Job level {after.JobLevel.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    // Each quest the new log changed says so; the objective's monster and the reward come from the offer this session
    // saw.
    private void OnQuestsChanged()
    {
        IReadOnlyList<QuestLogEntry> quests = m_watched!.Quests;
        IReadOnlyList<QuestLogEntry>? before = m_lastQuests;
        m_lastQuests = quests;
        if (before == null)
        {
            return;
        }

        ClientConnection? connection = m_client != null ? m_client.Connection : null;
        ClientContent? content = m_client != null ? m_client.Content : null;
        foreach (QuestLogEntry after in quests)
        {
            NpcQuestOffer? offer =
                connection != null && connection.TryGetQuestOffer(after.Quest, out NpcQuestOffer seen)
                    ? seen
                    : null;
            string? line = QuestMessages.Describe(Find(before, after.Quest), after, offer, content);
            if (line != null)
            {
                Add(line);
            }
        }
    }

    private static QuestLogEntry? Find(IReadOnlyList<QuestLogEntry> log, QuestDefinitionId quest)
    {
        foreach (QuestLogEntry entry in log)
        {
            if (entry.Quest == quest)
            {
                return entry;
            }
        }

        return null;
    }

    private void OnChangeApplied(InventoryDelta delta)
    {
        string? line = TradeMessages.Describe(delta, m_client != null ? m_client.Content : null);
        if (line != null)
        {
            Add(line);
        }
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel = Ui.CreatePanel(
            transform,
            new Vector2(0.5f, 0f),
            new Vector2(0f, BottomFor(0f)),
            Width,
            Padding);
        m_panel = panel.gameObject;
        m_panelRect = panel;
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
