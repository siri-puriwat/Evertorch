using System;
using System.Collections.Generic;
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
///     completed, a level-up, a job level-up, a raised statistic, and a learned skill level.
/// </summary>
public sealed class FeedbackLines : MonoBehaviour
{
    public const int MaxLines = 3;
    public const float LineSeconds = 4f;

    /// <summary>
    ///     The panel's width in canvas units: narrow enough to pass between the stick and the touch buttons.
    /// </summary>
    public const float Width = 600f;

    /// <summary>
    ///     The panel's height with all its lines, in canvas units: three lines of 24-point text with the padding and the
    ///     spacing.
    /// </summary>
    public const float MaxHeight = 2 * Padding + MaxLines * LineHeight + (MaxLines - 1) * LineSpacing;

    // Over the other in-world panels, under the login panel.
    private const int SortingOrder = 7;
    private const int Padding = 10;
    private const float HeightShare = 0.2f;
    private const float BarClearance = 16f;
    private const float LineHeight = 32f;
    private const float LineSpacing = 4f;

    private static readonly UiBuilder Ui = new(24f, LineHeight, 0f, LineSpacing);

    private readonly List<Line> m_lines = new();
    private readonly StringBuilder m_text = new();
    private GameClient? m_client;
    private ClientWorld? m_watched;
    private IReadOnlyList<QuestLogEntry>? m_lastQuests;
    private IReadOnlyList<SkillListEntry>? m_lastSkills;
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

    /// <summary>
    ///     How high the top of all three lines reaches, which the windows beside them stop above.
    /// </summary>
    public static float TopFor(float canvasHeight)
    {
        return BottomFor(canvasHeight) + MaxHeight;
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
            m_watched.SkillsChanged -= OnSkillsChanged;
            m_watched.Inventory.ChangeApplied -= OnChangeApplied;
            m_watched.QuestsChanged -= OnQuestsChanged;
        }

        m_watched = world;

        // A world's first quest log is its baseline, which is no news; it may have come before this frame.
        m_lastQuests = world != null && world.QuestLogsReceived > 0 ? world.Quests : null;
        m_lastSkills = world != null && world.SkillsReceivedAt > 0 ? world.Skills : null;
        if (world != null)
        {
            world.CommandRejectedReceived += OnRejected;
            world.ItemPickedUpReceived += OnPickedUp;
            world.LeveledUp += OnLeveledUp;
            world.SheetChanged += OnSheetChanged;
            world.SkillsChanged += OnSkillsChanged;
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
        bool isEquip = m_client != null
            && m_client.Connection != null
            && m_client.Connection.IsEquipSequence(rejected.CommandSequence);
        Add(RejectionMessages.Describe(rejected.Reason, isEquip));
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

    // A world's first skill list is its baseline, which is no news.
    private void OnSkillsChanged()
    {
        if (m_watched == null)
        {
            return;
        }

        IReadOnlyList<SkillListEntry> after = m_watched.Skills;
        foreach (string line in BuildMessages.DescribeSkills(m_lastSkills, after,
                     m_client != null ? m_client.Content : null))
        {
            Add(line);
        }

        m_lastSkills = after;
    }

    private void OnSheetChanged(CharacterSheet? before)
    {
        CharacterSheet? after = m_watched?.Sheet;
        if (after == null)
        {
            return;
        }

        foreach (string line in BuildMessages.Describe(before, after, m_client != null ? m_client.Content : null))
        {
            Add(line);
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
