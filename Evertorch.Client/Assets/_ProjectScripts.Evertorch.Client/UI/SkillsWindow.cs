using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The Skills window (Gameplay Systems §9; Prototype Content §2), at the NPC window's place and never beside it: the
///     skill points left, then the job's tree in its order, each skill with its level of its maximum, its prerequisite
///     while unmet, its description from the client package, and Learn while a point is left and the skill can take
///     it. Learn asks for one level through <see cref="GameClient" />, and only the next skill list moves the rows. It
///     closes when the world changes or the connection closes.
/// </summary>
public sealed class SkillsWindow : MonoBehaviour
{
    // With the NPC window, under the chat and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float RowSpacing = 6f;
    private const float NameWidth = 200f;
    private const float LevelWidth = 90f;
    private const float LearnWidth = 90f;
    private const float DescriptionFontSize = 16f;

    // Each skill's heading and its description, which takes two lines; an unmet prerequisite adds one.
    private const int RowsPerSkill = 3;

    private const float CloseWidth = 80f;

    // The padding, the heading with Close at its end, and the space between it and the list.
    private const float Chrome = 2 * Padding + RowHeight + RowSpacing;

    private static readonly UiBuilder Ui = new(22f, RowHeight, 0f, RowSpacing);

    private readonly StringBuilder m_text = new();
    private readonly List<GameObject> m_rowObjects = new();
    private readonly Dictionary<SkillDefinitionId, Button> m_learns = new();
    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_points;
    private GameObject? m_list;
    private LayoutElement? m_listLayout;
    private Transform? m_rows;
    private float m_listHeight = -1f;
    private float m_left = Margin;
    private ClientWorld? m_world;
    private IReadOnlyList<SkillListEntry>? m_shownSkills;
    private int m_shownPoints = -1;
    private bool m_wasDead;
    private bool m_hadContent;
    private bool m_isShown;
    private int m_listRows;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The rows as shown, one line each: the points, then for each skill "Strike Lv 1/5, Learn", its unmet
    ///     prerequisite, and its description; "Waiting for the server" until the sheet.
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

        Show(world, m_client!.Content);
        FitList();
    }

    public static SkillsWindow Create(GameClient client)
    {
        var root = new GameObject("SkillsWindow", typeof(RectTransform));
        root.SetActive(false);
        SkillsWindow window = root.AddComponent<SkillsWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    /// <summary>
    ///     Where the window sits on a canvas <paramref name="canvasHeight" /> units tall with <paramref name="skills" />
    ///     skills in its tree, in canvas units from the bottom-left corner: below the status bar, down to the top of the
    ///     chat, the skill bar, or the stick while the touch controls show, whichever is highest. Rows beyond
    ///     that scroll.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, int skills, bool isTouchShown)
    {
        float top = canvasHeight - (StatusBarHeight + Margin);
        float height = Chrome + ListHeightFor(canvasHeight, skills * RowsPerSkill, isTouchShown);
        return new Rect(NpcWindow.LeftFor(isTouchShown), top - height, Width, height);
    }

    /// <summary>
    ///     Whether <paramref name="skill" />'s Learn is shown and pressable: a point is left, the skill is below its
    ///     maximum, and its prerequisite is met.
    /// </summary>
    public bool IsLearnShown(SkillDefinitionId skill)
    {
        return m_learns.TryGetValue(skill, out Button? learn) && learn != null && learn.interactable;
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
        Show(world, m_client!.Content);
    }

    public void Close()
    {
        m_world = null;
        UiBuilder.SetActive(m_panel!, false);
    }

    // A row over a skill slot, the stick, or the chat would hide them or take the presses meant for them.
    private static float ListHeightFor(float canvasHeight, int rows, bool isTouchShown)
    {
        float needed = rows * RowHeight + Math.Max(0, rows - 1) * RowSpacing;
        float floor = Math.Max(
                Math.Max(SkillBar.Top, ChatPanel.TopFor(canvasHeight, isTouchShown)),
                isTouchShown ? TouchControls.StickBounds.yMax : 0f)
            + Margin;
        float room = canvasHeight - (StatusBarHeight + Margin) - Chrome - floor;
        return Math.Max(0f, Math.Min(needed, room));
    }

    private static string Description(ClientContent? content, SkillDefinitionId skill)
    {
        return content != null && content.TryGetSkill(skill, out ClientSkill? found) && found != null
            ? found.Description
            : string.Empty;
    }

    // Rebuilds the rows only when a new skill list arrived, the skill points changed, the character died or came back,
    // or the content to name them came: a sheet that changes nothing shown here, such as a kill's job experience, must
    // not destroy a Learn button under a press. The server refuses learning from the dead (3).
    private void Show(ClientWorld world, ClientContent? content)
    {
        bool hasContent = content != null;
        int points = world.Sheet?.SkillPoints ?? -1;
        if (m_isShown
            && world.Skills == m_shownSkills
            && points == m_shownPoints
            && world.IsLocalDead == m_wasDead
            && hasContent == m_hadContent)
        {
            return;
        }

        m_isShown = true;
        m_shownSkills = world.Skills;
        m_shownPoints = points;
        m_wasDead = world.IsLocalDead;
        m_hadContent = hasContent;
        ClearRows();
        m_text.Clear();
        m_listRows = 0;
        bool isReady = world.Sheet != null;
        UiBuilder.SetActive(m_list!, isReady);
        if (!isReady)
        {
            m_points!.text = "Waiting for the server";
            AppendText(m_points.text);
        }
        else
        {
            m_points!.text = BuildMessages.Points(world.Sheet!.SkillPoints);
            AppendText(m_points.text);
            foreach (SkillListEntry entry in world.Skills)
            {
                AddSkill(world.Skills, entry, world.Sheet.SkillPoints, content);
            }
        }

        Text = m_text.ToString();
        TextChanges++;
    }

    private void AddSkill(IReadOnlyList<SkillListEntry> tree, SkillListEntry entry, int points, ClientContent? content)
    {
        string name = BuildMessages.SkillName(content, entry.Skill);
        string? unmet = BuildMessages.UnmetPrerequisite(tree, entry, content);
        bool canLearn = !m_wasDead && points > 0 && entry.Level < entry.MaxLevel && unmet == null;

        GameObject heading = Ui.CreateRow($"Skill {entry.Skill.Value}", m_rows!);
        m_rowObjects.Add(heading);
        TMP_Text title = CreateCell(heading.transform, "Name", NameWidth, 1f);
        title.text = name;
        title.fontStyle = FontStyles.Bold;
        TMP_Text level = CreateCell(heading.transform, "Level", LevelWidth, 0f);
        level.text = BuildMessages.SkillLevel(entry);
        SkillDefinitionId skill = entry.Skill;
        GameClient client = m_client!;
        GameObject learn = Ui.CreateButton(
            canLearn ? "Learn" : string.Empty,
            heading.transform,
            () => client.LearnSkillLevel(skill));
        learn.name = $"Learn {skill.Value}";
        learn.GetComponent<LayoutElement>().preferredWidth = LearnWidth;
        Button button = learn.GetComponent<Button>();
        button.interactable = canLearn;
        m_learns[skill] = button;
        AppendText($"{name} {level.text}{(canLearn ? ", Learn" : string.Empty)}");

        m_listRows += RowsPerSkill;
        if (unmet != null)
        {
            AddLine(unmet, Ui.CreateLabel("Needs", m_rows!), RowHeight);
            m_listRows++;
        }

        string description = Description(content, skill);
        TMP_Text text = Ui.CreateLabel("Description", m_rows!);
        text.fontSize = DescriptionFontSize;
        AddLine(description, text, 2 * RowHeight);
    }

    private void AddLine(string line, TMP_Text label, float height)
    {
        label.text = line;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        m_rowObjects.Add(label.gameObject);
        if (line.Length > 0)
        {
            AppendText(line);
        }
    }

    private static TMP_Text CreateCell(Transform row, string objectName, float width, float flexibleWidth)
    {
        TMP_Text label = Ui.CreateLabel(objectName, row);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = label.fontSize;
        LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.flexibleWidth = flexibleWidth;
        return label;
    }

    private void AppendText(string text)
    {
        if (m_text.Length > 0)
        {
            m_text.Append('\n');
        }

        m_text.Append(text);
    }

    // Hidden at once and destroyed at the end of the frame, so the layout never shows the old rows beside the new.
    private void ClearRows()
    {
        foreach (GameObject row in m_rowObjects)
        {
            row.SetActive(false);
            Destroy(row);
        }

        m_rowObjects.Clear();
        m_learns.Clear();
    }

    // The canvas height follows the screen's shape, and the touch controls come and go.
    private void FitList()
    {
        float canvasHeight = ((RectTransform)transform).rect.height;
        bool isTouchShown = m_client!.Touch != null && m_client.Touch.IsVisible;
        PlaceLeft(isTouchShown);
        float height = ListHeightFor(canvasHeight, m_listRows, isTouchShown);
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
        title.text = "Skills";
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredWidth = NameWidth;
        m_points = Ui.CreateLabel("Points", heading.transform);
        m_points.alignment = TextAlignmentOptions.MidlineRight;
        m_points.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Ui.CreateButton("Close", heading.transform, Close).GetComponent<LayoutElement>().preferredWidth = CloseWidth;

        m_rows = CreateList(panel);
        m_panel.SetActive(false);
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
