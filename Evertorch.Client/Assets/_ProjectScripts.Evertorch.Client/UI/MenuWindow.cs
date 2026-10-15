using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The Menu (Prototype Content §2, §4), in the side slot, opened by the gamepad's Select or the Menu touch button:
///     Stats, Skills, and Equipment, a press on which opens that window in its place. While the window has the gamepad's
///     focus, a highlight shows the button South presses. It closes when the world changes or the connection closes.
/// </summary>
public sealed class MenuWindow : MonoBehaviour
{
    public const int StatsEntry = 0;
    public const int SkillsEntry = 1;
    public const int EquipmentEntry = 2;

    // With the side windows, under the chat and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float RowSpacing = 6f;
    private const float CloseWidth = 80f;

    // The padding, the heading with Close at its end, and the space between it and the list.
    private const float Chrome = 2 * Padding + RowHeight + RowSpacing;

    private static readonly string[] Entries = { "Stats", "Skills", "Equipment" };
    private static readonly UiBuilder Ui = new(20f, RowHeight, 0f, RowSpacing);

    private readonly Button[] m_buttons = new Button[Entries.Length];
    private readonly Image[] m_backgrounds = new Image[Entries.Length];
    private GameClient? m_client;
    private GameObject? m_panel;
    private ScrollRect? m_scroll;
    private LayoutElement? m_listLayout;
    private float m_listHeight = -1f;
    private float m_left = Margin;
    private ClientWorld? m_world;
    private bool m_isScrollPending;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The highlighted entry, from <see cref="StatsEntry" />; -1 while the window has no focus.
    /// </summary>
    public int Highlight { get; private set; } = -1;

    /// <summary>
    ///     The window as shown, one line each: "Menu", then each entry.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

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

        FitList();
        if (m_isScrollPending && Highlight >= 0)
        {
            m_isScrollPending = false;
            UiBuilder.ScrollIntoView(m_scroll!, (RectTransform)m_buttons[Highlight].transform);
        }
    }

    public static MenuWindow Create(GameClient client)
    {
        var root = new GameObject("MenuWindow", typeof(RectTransform));
        root.SetActive(false);
        MenuWindow window = root.AddComponent<MenuWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    /// <summary>
    ///     Where the window sits on a canvas <paramref name="canvasHeight" /> units tall, in canvas units from the
    ///     bottom-left corner, as the NPC window does; its entries scroll on the shortest canvas.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, bool isTouchShown)
    {
        float top = canvasHeight - (StatusBarHeight + Margin);
        float height = Chrome + ListHeightFor(canvasHeight, isTouchShown);
        return new Rect(NpcWindow.LeftFor(isTouchShown), top - height, Width, height);
    }

    public void Open()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null)
        {
            return;
        }

        m_world = world;
        UiBuilder.SetActive(m_panel!, true);
        SetHighlight(-1);
        Update();
    }

    public void Close()
    {
        m_world = null;
        SetHighlight(-1);
        UiBuilder.SetActive(m_panel!, false);
    }

    /// <summary>
    ///     Shows the gamepad's highlight on <paramref name="entry" />.
    /// </summary>
    public void Focus(int entry)
    {
        SetHighlight(Math.Max(0, Math.Min(entry, Entries.Length - 1)));
    }

    public void Unfocus()
    {
        SetHighlight(-1);
    }

    /// <summary>
    ///     Moves the highlight <paramref name="step" /> entries, stopping at the first and the last.
    /// </summary>
    public void MoveHighlight(int step)
    {
        if (Highlight >= 0)
        {
            SetHighlight(Math.Max(0, Math.Min(Highlight + step, Entries.Length - 1)));
        }
    }

    /// <summary>
    ///     Presses the highlighted entry, as a click would.
    /// </summary>
    public void Choose()
    {
        if (IsOpen && Highlight >= 0)
        {
            m_buttons[Highlight].onClick.Invoke();
        }
    }

    private static float ListHeightFor(float canvasHeight, bool isTouchShown)
    {
        float needed = Entries.Length * RowHeight + (Entries.Length - 1) * RowSpacing;
        float floor = Math.Max(
                Math.Max(SkillBar.Top, ChatPanel.TopFor(canvasHeight, isTouchShown)),
                isTouchShown ? TouchControls.StickBounds.yMax : 0f)
            + Margin;
        float room = canvasHeight - (StatusBarHeight + Margin) - Chrome - floor;
        return Math.Max(0f, Math.Min(needed, room));
    }

    private void SetHighlight(int entry)
    {
        Highlight = entry;
        for (int index = 0; index < m_backgrounds.Length; index++)
        {
            if (m_backgrounds[index] != null)
            {
                m_backgrounds[index].color = index == entry ? UiBuilder.AccentColor : UiBuilder.ControlColor;
            }
        }

        m_isScrollPending = entry >= 0;
    }

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
        title.text = "Menu";
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Ui.CreateButton("Close", heading.transform, Close).GetComponent<LayoutElement>().preferredWidth = CloseWidth;

        Transform rows = CreateList(panel);
        var text = new StringBuilder(title.text);
        UnityAction[] presses = { client.ToggleStats, client.ToggleSkills, client.ToggleEquipment };
        for (int index = 0; index < Entries.Length; index++)
        {
            GameObject button = Ui.CreateButton(Entries[index], rows, presses[index]);
            m_buttons[index] = button.GetComponent<Button>();
            m_backgrounds[index] = button.GetComponent<Image>();
            text.Append('\n').Append(Entries[index]);
        }

        Text = text.ToString();
        m_panel.SetActive(false);
    }

    // The entries scroll inside a list as tall as FitList allows, as the other side windows' rows do.
    private Transform CreateList(Transform panel)
    {
        var list = new GameObject("List", typeof(RectTransform));
        list.transform.SetParent(panel, false);
        list.AddComponent<RectMask2D>();
        list.AddComponent<Image>().color = Color.clear;
        m_listLayout = list.AddComponent<LayoutElement>();
        GameObject rows = Ui.CreateColumn("Rows", list.transform);
        var content = (RectTransform)rows.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        rows.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        m_scroll = list.AddComponent<ScrollRect>();
        m_scroll.content = content;
        m_scroll.horizontal = false;
        m_scroll.movementType = ScrollRect.MovementType.Clamped;
        m_scroll.scrollSensitivity = RowHeight;
        return rows.transform;
    }
}
}
