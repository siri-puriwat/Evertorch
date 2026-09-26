using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     The character's items as text (Prototype Content §2), along the right edge while in the world. A row whose item
///     has an action is a button that asks for it (<see cref="InventoryActions" />), and a worn row ends
///     "(equipped)". Icons would pull the optional icon keys into the Addressables key check.
/// </summary>
public sealed class InventoryWindow : MonoBehaviour
{
    // With the status bar and the target frame.
    private const int SortingOrder = 6;
    private const float Width = 300f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;

    private static readonly UiBuilder Ui = new(22f, 30f, 0f, 4f);

    private readonly StringBuilder m_text = new();
    private readonly List<GameObject> m_rowObjects = new();
    private GameClient? m_client;
    private GameObject? m_panel;
    private Transform? m_rows;
    private ClientInventory? m_shownInventory;
    private bool m_shownCurrent;
    private uint m_shownRevision;
    private bool m_hadContent;

    public int TextChanges { get; private set; }

    /// <summary>
    ///     The rows as shown, one line each.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        UiBuilder.SetActive(m_panel!, world != null);
        if (world != null)
        {
            Show(world.Inventory, m_client!.Content);
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
            button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
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
        m_rowObjects.Add(label.gameObject);
        AppendText(text);
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
        TMP_Text heading = Ui.CreateLabel("Heading", panel);
        heading.text = "Inventory";
        heading.fontStyle = FontStyles.Bold;
        m_rows = Ui.CreateColumn("Rows", panel).transform;
        m_panel.SetActive(false);
    }
}
}
