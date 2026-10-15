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
///     The Card window (Gameplay Systems §11.7; Prototype Content §2), in the side slot, opened by a press of a card's
///     bag row: the card's name, its description, what it fits, and a row for each unworn bag row it fits with a free
///     slot. A first press of a row arms the insertion and a second within <see cref="ArmSeconds" /> asks for it through
///     <see cref="GameClient" />; only the server's committed change moves the card. It closes when the world changes,
///     when the card's row is gone, or when the connection closes.
/// </summary>
public sealed class CardWindow : MonoBehaviour
{
    public const float ArmSeconds = 5f;

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

    private static readonly UiBuilder Ui = new(20f, RowHeight, 0f, RowSpacing);

    private readonly StringBuilder m_text = new();
    private readonly List<GameObject> m_rowObjects = new();
    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_name;
    private Transform? m_rows;
    private LayoutElement? m_listLayout;
    private float m_listHeight = -1f;
    private float m_left = Margin;
    private ClientWorld? m_world;
    private long m_card;
    private uint m_shownRevision;
    private bool m_isShown;
    private long m_armedRow;
    private float m_armedUntil;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The card's bag row the window shows; 0 while it is closed.
    /// </summary>
    public long CardRow => IsOpen ? m_card : 0;

    /// <summary>
    ///     The window as shown, one line each: the card's name, its description, what it fits, then each row.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null || world != m_world || world.IsHeld || !TryGetCard(world, out InventoryEntry card))
        {
            Close();
            return;
        }

        if (m_armedRow != 0 && Time.unscaledTime > m_armedUntil)
        {
            m_armedRow = 0;
            m_isShown = false;
        }

        Show(world.Inventory, card, m_client!.Content);
        FitList();
    }

    public static CardWindow Create(GameClient client)
    {
        var root = new GameObject("CardWindow", typeof(RectTransform));
        root.SetActive(false);
        CardWindow window = root.AddComponent<CardWindow>();
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
    ///     Shows the card in the bag's row <paramref name="cardRow" />.
    /// </summary>
    public void Open(long cardRow)
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null)
        {
            return;
        }

        m_world = world;
        m_card = cardRow;
        m_isShown = false;
        m_armedRow = 0;
        UiBuilder.SetActive(m_panel!, true);
        Update();
    }

    public void Close()
    {
        m_world = null;
        m_card = 0;
        m_armedRow = 0;
        UiBuilder.SetActive(m_panel!, false);
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

    private bool TryGetCard(ClientWorld world, out InventoryEntry card)
    {
        foreach (InventoryEntry row in world.Inventory.Rows)
        {
            if (row.InventoryItem == m_card)
            {
                card = row;
                return true;
            }
        }

        card = default;
        return false;
    }

    // Rewrites the rows only when the bag's revision, the card, or an armed row changed.
    private void Show(ClientInventory inventory, InventoryEntry card, ClientContent? content)
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
        m_text.Clear();
        ClientItem? item = null;
        content?.TryGetItem(card.Item, out item);
        m_name!.text = item != null ? item.DisplayName : card.Item.Value;
        AppendText(m_name.text);
        AddLine(item != null ? item.Description : string.Empty, true);
        EquipmentSlot fits = item != null ? item.Fits : EquipmentSlot.None;
        AddLine(CardMessages.Fits(fits), false);
        int listed = 0;
        foreach (InventoryEntry row in inventory.Rows)
        {
            int free = CardMessages.FreeSlots(content, row, fits);
            if (free <= 0)
            {
                continue;
            }

            long target = row.InventoryItem;
            string text = m_armedRow == target
                ? CardMessages.InsertPrompt
                : $"{ShopMessages.RowName(content, row.Item, row.RefineLevel, row.Cards)} · {free} free";
            GameObject button = Ui.CreateButton(text, m_rows!, () => Press(target));
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            FitOnOneLine(label);
            m_rowObjects.Add(button);
            AppendText(text);
            listed++;
        }

        if (listed == 0)
        {
            AddLine("Nothing it fits has a free slot", false);
        }

        Text = m_text.ToString();
    }

    // A first press arms the row; a second within ArmSeconds asks for the insertion.
    private void Press(long target)
    {
        if (m_armedRow == target && Time.unscaledTime <= m_armedUntil)
        {
            m_armedRow = 0;
            m_isShown = false;
            m_client!.InsertCardInto(m_card, target);
            return;
        }

        m_armedRow = target;
        m_armedUntil = Time.unscaledTime + ArmSeconds;
        m_isShown = false;
    }

    private void AddLine(string text, bool isDescription)
    {
        TMP_Text label = Ui.CreateLabel("Row", m_rows!);
        label.text = text;
        if (isDescription)
        {
            // A description of up to 240 characters wraps within two rows' height.
            label.textWrappingMode = TextWrappingModes.Normal;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = label.fontSize;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 2 * RowHeight + RowSpacing;
        }
        else
        {
            FitOnOneLine(label);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        }

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
        float height = ListHeightFor(canvasHeight, m_rowObjects.Count + 1, isTouchShown);
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
        m_name = Ui.CreateLabel("Name", heading.transform);
        m_name.fontStyle = FontStyles.Bold;
        m_name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        FitOnOneLine(m_name);
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
        ScrollRect scroll = list.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = RowHeight;
        return rows.transform;
    }
}
}
