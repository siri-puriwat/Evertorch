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
///     The character's coins and items as text (Prototype Content §2), along the right edge while in the world. The
///     coins share the heading's line above the rows, which scroll; a row whose item has an action is a button that asks
///     for it (<see cref="InventoryActions" />), and a worn row ends "(equipped)". Icons would pull the optional icon
///     keys into the Addressables key check.
/// </summary>
public sealed class InventoryWindow : MonoBehaviour
{
    // With the status bar and the target frame.
    private const int SortingOrder = 6;
    private const float Width = 300f;
    private const float Margin = 8f;

    /// <summary>
    ///     The window's left edge in canvas units, which the target frame beside the windows at the top left stops short
    ///     of.
    /// </summary>
    public const float Left = ClientUI.CanvasWidth - Margin - Width;

    private const float StatusBarHeight = 56f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float RowSpacing = 4f;

    private const float TitleWidth = 110f;

    // The padding, the heading, and the space below it.
    private const float Chrome = 2 * Padding + RowHeight + RowSpacing;

    private static readonly UiBuilder Ui = new(22f, RowHeight, 0f, RowSpacing);

    private readonly StringBuilder m_text = new();
    private readonly List<GameObject> m_rowObjects = new();
    private GameClient? m_client;
    private GameObject? m_panel;
    private Transform? m_rows;
    private TMP_Text? m_coins;
    private LayoutElement? m_list;
    private float m_listHeight = -1f;
    private ClientInventory? m_shownInventory;
    private bool m_shownCurrent;
    private uint m_shownRevision;
    private bool m_hadContent;

    public int TextChanges { get; private set; }

    /// <summary>
    ///     The rows as shown, one line each.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>
    ///     The coins as shown, "Coins: N"; empty until the inventory is current.
    /// </summary>
    public string CoinsText => m_coins != null ? m_coins.text : string.Empty;

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        UiBuilder.SetActive(m_panel!, world != null);
        if (world != null)
        {
            Show(world.Inventory, m_client!.Content);
            FitList();
        }
    }

    /// <summary>
    ///     Where the window sits on a canvas <paramref name="canvasHeight" /> units tall with <paramref name="rows" />
    ///     rows, in canvas units from the bottom-left corner: below the status bar, down to the touch controls' buttons
    ///     while they are shown and to the bottom margin otherwise. Rows beyond that scroll.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, int rows, bool isTouchShown)
    {
        float top = canvasHeight - (StatusBarHeight + Margin);
        float height = Chrome + ListHeightFor(canvasHeight, rows, isTouchShown);
        return new Rect(Left, top - height, Width, height);
    }

    // A row under a touch button would take the taps meant for it, and a press of a row equips or drinks.
    private static float ListHeightFor(float canvasHeight, int rows, bool isTouchShown)
    {
        float needed = rows * RowHeight + Math.Max(0, rows - 1) * RowSpacing;
        float floor = isTouchShown ? TouchControls.ButtonColumnBounds(ClientUI.CanvasWidth).yMax + Margin : Margin;
        float room = canvasHeight - (StatusBarHeight + Margin) - Chrome - floor;
        return Math.Max(0f, Math.Min(needed, room));
    }

    // The canvas height follows the screen's shape, and the touch controls come and go.
    private void FitList()
    {
        float canvasHeight = ((RectTransform)transform).rect.height;
        bool isTouchShown = m_client!.Touch != null && m_client.Touch.IsVisible;
        float height = ListHeightFor(canvasHeight, m_rowObjects.Count, isTouchShown);
        if (height != m_listHeight)
        {
            m_listHeight = height;
            m_list!.preferredHeight = height;
        }
    }

    public static InventoryWindow Create(GameClient client)
    {
        var root = new GameObject("InventoryWindow", typeof(RectTransform));
        root.SetActive(false);
        InventoryWindow window = root.AddComponent<InventoryWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    /// <summary>
    ///     Rebuilds the rows only when the inventory, its revision, or the names to show have changed.
    /// </summary>
    public void Show(ClientInventory inventory, ClientContent? content)
    {
        bool hasContent = content != null;
        if (inventory == m_shownInventory
            && inventory.IsCurrent == m_shownCurrent
            && inventory.Revision == m_shownRevision
            && hasContent == m_hadContent)
        {
            return;
        }

        m_shownInventory = inventory;
        m_shownCurrent = inventory.IsCurrent;
        m_shownRevision = inventory.Revision;
        m_hadContent = hasContent;
        ClearRows();
        m_text.Clear();
        m_coins!.text = inventory.IsCurrent ? $"Coins: {inventory.Coins}" : string.Empty;
        if (!inventory.IsCurrent)
        {
            AddLine("Waiting for the server");
        }
        else if (inventory.Rows.Count == 0)
        {
            AddLine("Empty");
        }
        else
        {
            foreach (InventoryEntry row in inventory.Rows)
            {
                AddRow(row, content);
            }
        }

        Text = m_text.ToString();
        TextChanges++;
    }

    private static string RowText(InventoryEntry row, ClientItem? item)
    {
        string name = item != null ? item.DisplayName : row.Item.Value;
        return row.Slot == EquipmentSlot.None ? $"{name} x {row.Quantity}" : $"{name} x {row.Quantity} (equipped)";
    }

    private void AddRow(InventoryEntry row, ClientContent? content)
    {
        ClientItem? item = null;
        if (content != null && content.TryGetItem(row.Item, out ClientItem? found))
        {
            item = found;
        }

        string text = RowText(row, item);
        if (item != null && InventoryActions.HasAction(item.Type))
        {
            GameClient client = m_client!;
            GameObject button = Ui.CreateButton(text, m_rows!, () => client.PressInventoryRow(row));
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            FitOnOneLine(label);
            m_rowObjects.Add(button);
            AppendText(text);
        }
        else
        {
            AddLine(text);
        }
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

    // A long row shrinks to fit its one line: wrapped, it would spill out of its row, and the list's mask cuts off what
    // spills past the list's edge.
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

    // Hidden at once and destroyed at the end of the frame, so the layout never shows the old rows beside the new.
    private void ClearRows()
    {
        foreach (GameObject row in m_rowObjects)
        {
            row.SetActive(false);
            Destroy(row);
        }

        m_rowObjects.Clear();
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel = Ui.CreatePanel(
            transform,
            Vector2.one,
            new Vector2(-Margin, -(StatusBarHeight + Margin)),
            Width,
            Padding);
        m_panel = panel.gameObject;
        // The coins share the heading's line, so the rows keep all the room there is on a short screen.
        GameObject heading = Ui.CreateRow("Heading", panel);
        TMP_Text title = Ui.CreateLabel("Title", heading.transform);
        title.text = "Inventory";
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredWidth = TitleWidth;
        m_coins = Ui.CreateLabel("Coins", heading.transform);
        m_coins.alignment = TextAlignmentOptions.MidlineRight;
        m_coins.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        FitOnOneLine(m_coins);
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
        m_list = list.AddComponent<LayoutElement>();
        GameObject rows = Ui.CreateColumn("Rows", list.transform);
        var content = (RectTransform)rows.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        rows.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = list.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = RowHeight;
        return rows.transform;
    }
}
}
