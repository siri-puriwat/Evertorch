using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     The character's items as text (Prototype Content §2), along the right edge while in the world. Icons would pull
///     the optional icon keys into the Addressables key check.
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
    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_rows;
    private ClientInventory? m_shownInventory;
    private bool m_shownCurrent;
    private uint m_shownRevision;
    private bool m_hadContent;

    public int TextChanges { get; private set; }

    public string Text => m_rows != null ? m_rows.text : string.Empty;

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
    ///     Rewrites the list only when the inventory, its revision, or the names to show have changed.
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
        m_text.Clear();
        if (!inventory.IsCurrent)
        {
            m_text.Append("Waiting for the server");
        }
        else if (inventory.Rows.Count == 0)
        {
            m_text.Append("Empty");
        }
        else
        {
            foreach (InventoryEntry row in inventory.Rows)
            {
                if (m_text.Length > 0)
                {
                    m_text.Append('\n');
                }

                m_text.Append($"{ItemName(content, row.Item)} x {row.Quantity}");
            }
        }

        m_rows!.text = m_text.ToString();
        TextChanges++;
    }

    private static string ItemName(ClientContent? content, ItemDefinitionId item)
    {
        return content != null && content.TryGetItem(item, out ClientItem? found) && found != null
            ? found.DisplayName
            : item.Value;
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
        m_rows = Ui.CreateLabel("Rows", panel);
        m_panel.SetActive(false);
    }
}
}
