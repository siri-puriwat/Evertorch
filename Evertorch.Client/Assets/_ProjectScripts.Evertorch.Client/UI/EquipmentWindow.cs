using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The Equipment window (Gameplay Systems §11.1; Prototype Content §2), in the side slot, which replaces and is
///     replaced as the Stats window is: the eight positions in order, each a button naming what it holds, its cards on
///     lines of their own, or "empty". A press on a position lists below it "Take off" when it holds a row and each unworn
///     bag row that may go there; a press on one asks for it through <see cref="GameClient" />, and only the server's
///     committed change moves the rows. Opened from an unworn accessory's bag row, it holds that row chosen until a press,
///     and a press on either accessory position wears it there. While the window has the gamepad's focus, a highlight
///     shows the button South presses. It closes when the world changes or the connection closes.
/// </summary>
public sealed class EquipmentWindow : MonoBehaviour
{
    // A press's row: the position itself, Take off, or a bag row by its ID.
    private const long PositionPress = 0;
    private const long TakeOffPress = -1;

    // With the side windows, under the chat and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float RowSpacing = 6f;
    private const float CloseWidth = 80f;
    private const int ChoiceIndent = 24;

    // The padding, the heading with Close at its end, and the space between it and the list.
    private const float Chrome = 2 * Padding + RowHeight + RowSpacing;

    private static readonly UiBuilder Ui = new(20f, RowHeight, 0f, RowSpacing);

    private readonly StringBuilder m_text = new();
    private readonly List<GameObject> m_rowObjects = new();
    private readonly List<Press> m_presses = new();
    private GameClient? m_client;
    private GameObject? m_panel;
    private Transform? m_rows;
    private ScrollRect? m_scroll;
    private LayoutElement? m_listLayout;
    private float m_listHeight = -1f;
    private float m_left = Margin;
    private ClientWorld? m_world;
    private uint m_shownRevision;
    private bool m_isShown;
    private EquipmentSlot m_listed;
    private long m_chosenRow;
    private bool m_isFocused;
    private EquipmentSlot m_highlightSlot;
    private long m_highlightRow;
    private bool m_isScrollPending;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The unworn accessory's bag row a press on an accessory position wears; 0 when none is chosen.
    /// </summary>
    public long ChosenRow => IsOpen ? m_chosenRow : 0;

    /// <summary>
    ///     The position whose choices are listed; <see cref="EquipmentSlot.None" /> when none is.
    /// </summary>
    public EquipmentSlot Listed => IsOpen ? m_listed : EquipmentSlot.None;

    /// <summary>
    ///     The highlighted button's text; empty while the window has no focus.
    /// </summary>
    public string Highlighted
    {
        get
        {
            int index = HighlightIndex();
            return m_isFocused && index >= 0 ? m_presses[index].Text : string.Empty;
        }
    }

    /// <summary>
    ///     The window as shown, one line each: "Equipment", the choice of an accessory's position while one is chosen,
    ///     then each position, its cards, and the choices listed under it.
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

        if (m_chosenRow != 0 && !IsUnwornAccessory(world.Inventory.Rows, m_client!.Content, m_chosenRow))
        {
            m_chosenRow = 0;
            m_isShown = false;
        }

        Show(world.Inventory, m_client!.Content);
        FitList();
        if (m_isScrollPending)
        {
            m_isScrollPending = false;
            int index = HighlightIndex();
            if (m_isFocused && index >= 0)
            {
                UiBuilder.ScrollIntoView(m_scroll!, (RectTransform)m_presses[index].Button.transform);
            }
        }
    }

    public static EquipmentWindow Create(GameClient client)
    {
        var root = new GameObject("EquipmentWindow", typeof(RectTransform));
        root.SetActive(false);
        EquipmentWindow window = root.AddComponent<EquipmentWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    /// <summary>
    ///     Where the window sits on a canvas <paramref name="canvasHeight" /> units tall, holding
    ///     <paramref name="rows" /> rows, in canvas units from the bottom-left corner, as the NPC window does.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, int rows, bool isTouchShown)
    {
        float top = canvasHeight - (StatusBarHeight + Margin);
        float height = Chrome + ListHeightFor(canvasHeight, rows, isTouchShown);
        return new Rect(NpcWindow.LeftFor(isTouchShown), top - height, Width, height);
    }

    /// <summary>
    ///     Shows the eight positions, with <paramref name="chosenRow" />, an unworn accessory's bag row, chosen when it is
    ///     not 0.
    /// </summary>
    public void Open(long chosenRow = 0)
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null)
        {
            return;
        }

        m_world = world;
        m_listed = EquipmentSlot.None;
        m_chosenRow = chosenRow;
        m_isShown = false;
        UiBuilder.SetActive(m_panel!, true);
        Update();
    }

    public void Close()
    {
        m_world = null;
        m_listed = EquipmentSlot.None;
        m_chosenRow = 0;
        m_isFocused = false;
        UiBuilder.SetActive(m_panel!, false);
    }

    /// <summary>
    ///     Shows the gamepad's highlight, on the weapon's position at first.
    /// </summary>
    public void Focus()
    {
        m_isFocused = true;
        m_highlightSlot = EquipmentSlot.Weapon;
        m_highlightRow = PositionPress;
        ShowHighlight();
    }

    public void Unfocus()
    {
        m_isFocused = false;
        ShowHighlight();
    }

    /// <summary>
    ///     Moves the highlight <paramref name="step" /> buttons, stopping at the first and the last.
    /// </summary>
    public void MoveHighlight(int step)
    {
        int index = HighlightIndex();
        if (!m_isFocused || index < 0)
        {
            return;
        }

        Press press = m_presses[Math.Max(0, Math.Min(index + step, m_presses.Count - 1))];
        m_highlightSlot = press.Slot;
        m_highlightRow = press.Row;
        ShowHighlight();
    }

    /// <summary>
    ///     Presses the highlighted button, as a click would.
    /// </summary>
    public void Choose()
    {
        int index = HighlightIndex();
        if (IsOpen && m_isFocused && index >= 0)
        {
            m_presses[index].Button.onClick.Invoke();
        }
    }

    /// <summary>
    ///     Backs out one level: a listed position's choices close, and the highlight goes back to the position. False when
    ///     nothing is listed, so the Menu is the level above.
    /// </summary>
    public bool Back()
    {
        if (m_listed == EquipmentSlot.None)
        {
            return false;
        }

        m_highlightSlot = m_listed;
        m_highlightRow = PositionPress;
        m_listed = EquipmentSlot.None;
        m_isShown = false;
        return true;
    }

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

    private static bool IsUnwornAccessory(IReadOnlyList<InventoryEntry> rows, ClientContent? content, long chosenRow)
    {
        foreach (InventoryEntry row in rows)
        {
            if (row.InventoryItem == chosenRow)
            {
                return row.Slot == EquipmentSlot.None
                    && content != null
                    && content.TryGetItem(row.Item, out ClientItem? item)
                    && item != null
                    && item.Type == ItemType.Accessory;
            }
        }

        return false;
    }

    // Rewrites the rows only when the bag's revision, the listed position, or the chosen row changed.
    private void Show(ClientInventory inventory, ClientContent? content)
    {
        if (m_isShown && inventory.Revision == m_shownRevision)
        {
            return;
        }

        m_isShown = true;
        m_shownRevision = inventory.Revision;
        foreach (GameObject row in m_rowObjects)
        {
            Destroy(row);
        }

        m_rowObjects.Clear();
        m_presses.Clear();
        m_text.Clear();
        AppendText("Equipment");
        IReadOnlyList<InventoryEntry> rows = inventory.Rows;
        if (m_chosenRow != 0)
        {
            AddLine(EquipmentMessages.ChoosePosition(content, rows, m_chosenRow));
        }

        foreach (EquipmentSlot slot in EquipmentPositions.All)
        {
            ShowPosition(content, rows, slot);
        }

        Text = m_text.ToString();
        ShowHighlight();
    }

    private void ShowPosition(ClientContent? content, IReadOnlyList<InventoryEntry> rows, EquipmentSlot slot)
    {
        InventoryEntry? worn = EquipmentMessages.WornIn(rows, slot);
        AddPress(EquipmentMessages.Position(content, slot, worn), slot, PositionPress, 0, () => PressPosition(slot));
        if (worn.HasValue)
        {
            foreach (ItemDefinitionId card in worn.Value.Cards)
            {
                AddLine($"· {ShopMessages.ItemName(content, card)}");
            }
        }

        if (slot != m_listed)
        {
            return;
        }

        if (worn.HasValue)
        {
            AddPress("Take off", slot, TakeOffPress, ChoiceIndent, () => PressTakeOff(slot));
        }

        foreach (InventoryEntry choice in EquipmentMessages.ChoicesFor(content, rows, slot))
        {
            long row = choice.InventoryItem;
            AddPress(
                ShopMessages.RowName(content, choice.Item, choice.RefineLevel, choice.Cards),
                slot,
                row,
                ChoiceIndent,
                () => PressChoice(slot, row));
        }
    }

    // With an accessory chosen, either accessory position wears it; otherwise a press lists or closes the position's
    // choices.
    private void PressPosition(EquipmentSlot slot)
    {
        long chosen = m_chosenRow;
        m_chosenRow = 0;
        m_isShown = false;
        if (chosen != 0 && EquipmentPositions.Fits(ItemType.Accessory, slot))
        {
            m_listed = EquipmentSlot.None;
            m_client!.EquipInto(chosen, slot);
            return;
        }

        m_listed = m_listed == slot ? EquipmentSlot.None : slot;
    }

    private void PressTakeOff(EquipmentSlot slot)
    {
        CloseList(slot);
        m_client!.TakeOff(slot);
    }

    private void PressChoice(EquipmentSlot slot, long row)
    {
        CloseList(slot);
        m_client!.EquipInto(row, slot);
    }

    private void CloseList(EquipmentSlot slot)
    {
        m_listed = EquipmentSlot.None;
        m_highlightSlot = slot;
        m_highlightRow = PositionPress;
        m_isShown = false;
    }

    // The highlight follows its button through a rewrite; when that button is gone, it falls back to its position.
    private int HighlightIndex()
    {
        int position = -1;
        for (int index = 0; index < m_presses.Count; index++)
        {
            Press press = m_presses[index];
            if (press.Slot != m_highlightSlot)
            {
                continue;
            }

            if (press.Row == m_highlightRow)
            {
                return index;
            }

            if (press.Row == PositionPress)
            {
                position = index;
            }
        }

        return position;
    }

    private void ShowHighlight()
    {
        int highlight = m_isFocused ? HighlightIndex() : -1;
        for (int index = 0; index < m_presses.Count; index++)
        {
            m_presses[index].Background.color = index == highlight ? UiBuilder.AccentColor : UiBuilder.ControlColor;
        }

        m_isScrollPending = highlight >= 0;
    }

    private void AddPress(string text, EquipmentSlot slot, long row, int indent, UnityAction onClick)
    {
        GameObject parent = Ui.CreateRow(text, m_rows!);
        parent.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(indent, 0, 0, 0);
        GameObject button = Ui.CreateButton(text, parent.transform, onClick);
        button.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        label.alignment = TextAlignmentOptions.MidlineLeft;
        FitOnOneLine(label);
        m_rowObjects.Add(parent);
        m_presses.Add(new Press(button.GetComponent<Button>(), button.GetComponent<Image>(), slot, row, text));
        AppendText(text);
    }

    private void AddLine(string text)
    {
        TMP_Text label = Ui.CreateLabel("Row", m_rows!);
        label.text = text;
        FitOnOneLine(label);
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        m_rowObjects.Add(label.gameObject);
        AppendText(text);
    }

    private static void FitOnOneLine(TMP_Text label)
    {
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = label.fontSize;
    }

    private void AppendText(string text)
    {
        if (m_text.Length > 0)
        {
            m_text.Append('\n');
        }

        m_text.Append(text);
    }

    private void FitList()
    {
        float canvasHeight = ((RectTransform)transform).rect.height;
        bool isTouchShown = m_client!.Touch != null && m_client.Touch.IsVisible;
        PlaceLeft(isTouchShown);
        float height = ListHeightFor(canvasHeight, m_rowObjects.Count, isTouchShown);
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
        title.text = "Equipment";
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Ui.CreateButton("Close", heading.transform, Close).GetComponent<LayoutElement>().preferredWidth = CloseWidth;
        m_rows = CreateList(panel);
        m_panel.SetActive(false);
    }

    // The rows scroll inside a list as tall as FitList allows.
    private Transform CreateList(Transform panel)
    {
        var list = new GameObject("List", typeof(RectTransform));
        list.transform.SetParent(panel, false);
        list.AddComponent<RectMask2D>();

        // Clear, so that a drag anywhere in the list scrolls it, between the rows too.
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

    private readonly struct Press
    {
        public Press(Button button, Image background, EquipmentSlot slot, long row, string text)
        {
            Button = button;
            Background = background;
            Slot = slot;
            Row = row;
            Text = text;
        }

        public Button Button { get; }

        public Image Background { get; }

        public EquipmentSlot Slot { get; }

        public long Row { get; }

        public string Text { get; }
    }
}
}
