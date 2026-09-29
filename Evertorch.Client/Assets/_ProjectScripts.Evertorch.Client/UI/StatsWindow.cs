using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The Stats window (Gameplay Systems §2; Prototype Content §2), at the NPC window's place and never beside it: the
///     stat points left, each primary statistic with its value, its next cost, and "+" while the points cover it, and
///     the derived statistics, all from the owner's <see cref="CharacterSheet" />. A "+" asks for one raise through
///     <see cref="GameClient" />, and only the next sheet moves the rows. It closes when the world changes or the
///     connection closes.
/// </summary>
public sealed class StatsWindow : MonoBehaviour
{
    // With the NPC window, under the feedback lines and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float RowSpacing = 6f;
    private const float NameWidth = 200f;
    private const float StatWidth = 150f;
    private const float RaiseWidth = 60f;

    // Six statistics and four rows of two derived ones.
    private const int ListRows = CharacterSheet.StatCount + 4;

    private const float CloseWidth = 80f;

    // The padding, the heading with Close at its end, and the space between it and the list.
    private const float Chrome = 2 * Padding + RowHeight + RowSpacing;

    private static readonly UiBuilder Ui = new(22f, RowHeight, 0f, RowSpacing);

    private readonly StringBuilder m_text = new();
    private readonly TMP_Text[] m_values = new TMP_Text[CharacterSheet.StatCount];
    private readonly TMP_Text[] m_costs = new TMP_Text[CharacterSheet.StatCount];
    private readonly Button[] m_raises = new Button[CharacterSheet.StatCount];
    private readonly TMP_Text[] m_derived = new TMP_Text[8];
    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_points;
    private GameObject? m_list;
    private LayoutElement? m_listLayout;
    private float m_listHeight = -1f;
    private float m_left = Margin;
    private ClientWorld? m_world;
    private CharacterSheet? m_shownSheet;
    private bool m_isShown;
    private bool m_wasDead;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The rows as shown, one line each: the points, then "AGI 6, next 2, +" for each statistic, then each derived
    ///     statistic; "Waiting for the server" until the first sheet.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    public int TextChanges { get; private set; }

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null || world != m_world)
        {
            Close();
            return;
        }

        Show(world.Sheet, world.IsLocalDead);
        FitList();
    }

    public static StatsWindow Create(GameClient client)
    {
        var root = new GameObject("StatsWindow", typeof(RectTransform));
        root.SetActive(false);
        StatsWindow window = root.AddComponent<StatsWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    /// <summary>
    ///     Where the window sits on a canvas <paramref name="canvasHeight" /> units tall, in canvas units from the
    ///     bottom-left corner: below the status bar, down to the top of the feedback lines, the skill bar, or the stick
    ///     while the touch controls show, whichever is highest. Rows beyond that scroll.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, bool isTouchShown)
    {
        float top = canvasHeight - (StatusBarHeight + Margin);
        float height = Chrome + ListHeightFor(canvasHeight, isTouchShown);
        return new Rect(NpcWindow.LeftFor(isTouchShown), top - height, Width, height);
    }

    /// <summary>
    ///     Whether the "+" of <paramref name="stat" /> is shown and pressable: the points left cover its next cost.
    /// </summary>
    public bool IsRaiseShown(PrimaryStat stat)
    {
        Button raise = m_raises[(int)stat - 1];
        return raise != null && raise.interactable;
    }

    public void Open()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null)
        {
            return;
        }

        m_world = world;
        m_isShown = false;
        UiBuilder.SetActive(m_panel!, true);
        Show(world.Sheet, world.IsLocalDead);
    }

    public void Close()
    {
        m_world = null;
        UiBuilder.SetActive(m_panel!, false);
    }

    // A row over a skill slot, the stick, or the feedback lines would hide them or take the presses meant for them.
    private static float ListHeightFor(float canvasHeight, bool isTouchShown)
    {
        float needed = ListRows * RowHeight + (ListRows - 1) * RowSpacing;
        float floor = Math.Max(
                Math.Max(SkillBar.Top, FeedbackLines.TopFor(canvasHeight)),
                isTouchShown ? TouchControls.StickBounds.yMax : 0f)
            + Margin;
        float room = canvasHeight - (StatusBarHeight + Margin) - Chrome - floor;
        return Math.Max(0f, Math.Min(needed, room));
    }

    private static PrimaryStat StatAt(int index)
    {
        return (PrimaryStat)(index + 1);
    }

    // Rewrites the rows only when a new sheet arrived, each message replacing the sheet, or the character died or came
    // back: the server refuses a raise from the dead (3), so none is offered then.
    private void Show(CharacterSheet? sheet, bool isDead)
    {
        if (m_isShown && sheet == m_shownSheet && isDead == m_wasDead)
        {
            return;
        }

        m_isShown = true;
        m_shownSheet = sheet;
        m_wasDead = isDead;
        m_text.Clear();
        UiBuilder.SetActive(m_list!, sheet != null);
        if (sheet == null)
        {
            m_points!.text = "Waiting for the server";
            AppendText(m_points.text);
        }
        else
        {
            ShowSheet(sheet);
        }

        Text = m_text.ToString();
        TextChanges++;
    }

    private void ShowSheet(CharacterSheet sheet)
    {
        m_points!.text = BuildMessages.Points(sheet.StatPoints);
        AppendText(m_points.text);
        for (int index = 0; index < CharacterSheet.StatCount; index++)
        {
            CharacterSheetStat stat = sheet.Stats[index];
            bool canRaise = !m_wasDead && stat.NextCost > 0 && stat.NextCost <= sheet.StatPoints;
            m_values[index].text = BuildMessages.StatValue(StatAt(index), stat);
            m_costs[index].text = BuildMessages.NextCost(stat);
            m_raises[index].interactable = canRaise;
            m_raises[index].GetComponentInChildren<TMP_Text>().text = canRaise ? "+" : string.Empty;
            AppendText($"{m_values[index].text}, {m_costs[index].text}{(canRaise ? ", +" : string.Empty)}");
        }

        IReadOnlyList<string> derived = BuildMessages.Derived(sheet);
        for (int index = 0; index < derived.Count; index++)
        {
            m_derived[index].text = derived[index];
            AppendText(derived[index]);
        }
    }

    private void AppendText(string text)
    {
        if (m_text.Length > 0)
        {
            m_text.Append('\n');
        }

        m_text.Append(text);
    }

    // The canvas height follows the screen's shape, and the touch controls come and go.
    private void FitList()
    {
        float canvasHeight = ((RectTransform)transform).rect.height;
        bool isTouchShown = m_client!.Touch != null && m_client.Touch.IsVisible;
        PlaceLeft(isTouchShown);
        float height = ListHeightFor(canvasHeight, isTouchShown);
        if (height != m_listHeight)
        {
            m_listHeight = height;
            m_listLayout!.preferredHeight = height;
        }
    }

    // Beside the touch window buttons while they show, as the NPC window is (finding C2 of the Milestone 9 review).
    private void PlaceLeft(bool isTouchShown)
    {
        float left = NpcWindow.LeftFor(isTouchShown);
        if (left != m_left)
        {
            m_left = left;
            var panel = (RectTransform)m_panel!.transform;
            panel.anchoredPosition = new Vector2(left, panel.anchoredPosition.y);
        }
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel = Ui.CreatePanel(
            transform,
            new Vector2(0f, 1f),
            new Vector2(Margin, -(StatusBarHeight + Margin)),
            Width,
            Padding);
        m_panel = panel.gameObject;

        GameObject heading = Ui.CreateRow("Heading", panel);
        TMP_Text title = Ui.CreateLabel("Name", heading.transform);
        title.text = "Stats";
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredWidth = NameWidth;
        m_points = Ui.CreateLabel("Points", heading.transform);
        m_points.alignment = TextAlignmentOptions.MidlineRight;
        m_points.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Ui.CreateButton("Close", heading.transform, Close).GetComponent<LayoutElement>().preferredWidth = CloseWidth;

        Transform rows = CreateList(panel);
        for (int index = 0; index < CharacterSheet.StatCount; index++)
        {
            CreateStatRow(rows, index);
        }

        for (int row = 0; row < m_derived.Length / 2; row++)
        {
            GameObject line = Ui.CreateRow($"Derived {row}", rows);
            m_derived[2 * row] = CreateCell(line.transform, "Left", 0f, 1f);
            m_derived[2 * row + 1] = CreateCell(line.transform, "Right", 0f, 1f);
        }

        m_panel.SetActive(false);
    }

    private void CreateStatRow(Transform rows, int index)
    {
        PrimaryStat stat = StatAt(index);
        GameObject line = Ui.CreateRow($"Stat {BuildMessages.StatName(stat)}", rows);
        m_values[index] = CreateCell(line.transform, "Value", StatWidth, 0f);
        m_costs[index] = CreateCell(line.transform, "Cost", 0f, 1f);
        GameClient client = m_client!;
        GameObject raise = Ui.CreateButton("+", line.transform, () => client.RaiseStat(stat));
        raise.name = $"Raise {BuildMessages.StatName(stat)}";
        raise.GetComponent<LayoutElement>().preferredWidth = RaiseWidth;
        m_raises[index] = raise.GetComponent<Button>();
    }

    private static TMP_Text CreateCell(Transform row, string objectName, float width, float flexibleWidth)
    {
        TMP_Text label = Ui.CreateLabel(objectName, row);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.flexibleWidth = flexibleWidth;
        return label;
    }

    // The rows scroll inside a list as tall as FitList allows, as the NPC window's do.
    private Transform CreateList(Transform panel)
    {
        m_list = new GameObject("List", typeof(RectTransform));
        m_list.transform.SetParent(panel, false);
        m_list.AddComponent<RectMask2D>();
        m_list.AddComponent<Image>().color = Color.clear;
        m_listLayout = m_list.AddComponent<LayoutElement>();
        GameObject rows = Ui.CreateColumn("Rows", m_list.transform);
        var content = (RectTransform)rows.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        rows.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = m_list.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = RowHeight;
        return rows.transform;
    }
}
}
