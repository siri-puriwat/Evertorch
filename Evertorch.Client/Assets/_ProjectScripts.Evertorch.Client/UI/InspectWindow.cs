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
///     The Inspect window (Gameplay Systems §11.7; Prototype Content §2), in the side slot, opened by the server's answer
///     to an inspection: the inspected player's name, then the worn weapon and armor, each named with its refine level,
///     its cards, and its slots, and each card on a line of its own. It shows the answer as it came and closes when the
///     world changes or the connection closes.
/// </summary>
public sealed class InspectWindow : MonoBehaviour
{
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

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The window as shown, one line each: the player's name, then each line of the list.
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
    }

    public static InspectWindow Create(GameClient client)
    {
        var root = new GameObject("InspectWindow", typeof(RectTransform));
        root.SetActive(false);
        InspectWindow window = root.AddComponent<InspectWindow>();
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
    ///     Shows what the player named <paramref name="name" /> wears, as <paramref name="shown" /> says.
    /// </summary>
    public void Open(InspectedEquipment shown, string name)
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null || shown == null)
        {
            return;
        }

        m_world = world;
        UiBuilder.SetActive(m_panel!, true);
        Show(shown, name, m_client!.Content);
        Update();
    }

    public void Close()
    {
        m_world = null;
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

    private void Show(InspectedEquipment shown, string name, ClientContent? content)
    {
        foreach (GameObject row in m_rowObjects)
        {
            Destroy(row);
        }

        m_rowObjects.Clear();
        m_text.Clear();
        m_name!.text = name;
        AppendText(name);
        foreach (string line in InspectMessages.Lines(content, shown))
        {
            AddLine(line);
        }

        Text = m_text.ToString();
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
