using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client
{
/// <summary>
///     The window of the NPC the player walked up to (Gameplay Systems §2.2, §6.1, §11.3; Prototype Content §2): the
///     NPC's name and Close; for an NPC that trades, the coins, a Buy list of what it sells at its price, and a Sell list
///     of the character's rows it buys, not worn, at what one fetches; for each quest the NPC gives, its objective, its
///     reward, and where the character stands with it; and for the Guildmaster, "Reset all points". A Buy press buys
///     one; a Sell press sells one, and a stack's All sells the row; Accept and Turn in ask for the quest; the reset
///     asks for a second press within <see cref="ResetConfirmSeconds" />, since it undoes every choice. The presses go
///     through <see cref="GameClient" />, and only what the server commits moves the lists. It closes once the NPC is
///     drawn beyond its range, the player dies, the map changes, or the connection closes.
/// </summary>
public sealed class NpcWindow : MonoBehaviour
{
    /// <summary>
    ///     How long "Press again to reset" waits for the second press.
    /// </summary>
    public const float ResetConfirmSeconds = 5f;

    // With the status bar and the target frame, under the feedback lines and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float RowSpacing = 6f;
    private const float NameWidth = 200f;
    private const float AllWidth = 130f;

    private const float CloseWidth = 80f;

    // The padding, the heading with Close at its end, and the space between it and the list.
    private const float Chrome = 2 * Padding + RowHeight + RowSpacing;

    /// <summary>
    ///     The right edge of the windows at the top left, this one, the Stats window, and the Skills window, in canvas
    ///     units.
    /// </summary>
    public const float Right = Margin + Width;

    private static readonly UiBuilder Ui = new(22f, RowHeight, 0f, RowSpacing);

    private readonly StringBuilder m_text = new();
    private readonly List<GameObject> m_rowObjects = new();
    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_name;
    private TMP_Text? m_coins;
    private GameObject? m_list;
    private LayoutElement? m_listLayout;
    private Transform? m_rows;
    private float m_listHeight = -1f;
    private ClientWorld? m_world;
    private NpcServices? m_shownServices;
    private IReadOnlyList<QuestLogEntry>? m_shownQuests;
    private ClientInventory? m_shownInventory;
    private bool m_shownCurrent;
    private uint m_shownRevision;
    private bool m_hadContent;
    private bool m_isResetArmed;
    private float m_resetArmedUntil;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The NPC the window is open for; default while it is closed.
    /// </summary>
    public EntityId Npc { get; private set; }

    public string ShownName => m_name != null ? m_name.text : string.Empty;

    /// <summary>
    ///     The coins as shown, "Coins: N"; empty for an NPC that does not trade and until the inventory is current.
    /// </summary>
    public string CoinsText => m_coins != null ? m_coins.text : string.Empty;

    /// <summary>
    ///     The lists as shown, one line each; empty for an NPC that neither trades nor gives a quest.
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
        if (m_client == null
            || world == null
            || world != m_world
            || world.IsLocalDead
            || !m_client.RemoteViews.TryGetValue(Npc, out EntityView? view)
            || view == null)
        {
            Close();
            return;
        }

        // Measured as the target frame measures: from the predicted player to where the NPC is drawn.
        WorldPosition self = world.Predictor.Position;
        Vector3 at = view.transform.position;
        if (new Vector2(at.x - self.X, at.z - self.Z).magnitude > NpcInteraction.Range)
        {
            Close();
            return;
        }

        if (m_isResetArmed && Time.unscaledTime > m_resetArmedUntil)
        {
            ArmReset(false);
        }

        Show(world, m_client.Content);
        FitList();
    }

    public static NpcWindow Create(GameClient client)
    {
        var root = new GameObject("NpcWindow", typeof(RectTransform));
        root.SetActive(false);
        NpcWindow window = root.AddComponent<NpcWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    /// <summary>
    ///     Where a trading NPC's window sits on a canvas <paramref name="canvasHeight" /> units tall with
    ///     <paramref name="rows" /> rows in its lists, in canvas units from the bottom-left corner: below the status bar,
    ///     down to the top of the feedback lines, the skill bar, or the stick while the touch controls show, whichever is
    ///     highest. Rows beyond that scroll.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, int rows, bool isTouchShown)
    {
        float top = canvasHeight - (StatusBarHeight + Margin);
        float height = Chrome + ListHeightFor(canvasHeight, rows, isTouchShown);
        return new Rect(Margin, top - height, Width, height);
    }

    /// <summary>
    ///     Opens the window for <paramref name="npc" /> in the current world, showing its name, and its lists when it
    ///     trades.
    /// </summary>
    public void Open(EntityId npc)
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null || !world.Remotes.TryGetValue(npc, out RemoteEntity? remote))
        {
            return;
        }

        m_world = world;
        Npc = npc;
        m_name!.text = NpcName(m_client!.Content, remote.DefinitionId);
        UiBuilder.SetActive(m_panel!, true);
        Show(world, m_client.Content);
    }

    public void Close()
    {
        m_world = null;
        Npc = default;
        m_isResetArmed = false;
        UiBuilder.SetActive(m_panel!, false);
    }

    // A row over a skill slot, the stick, or the feedback lines would hide them or take the presses meant for them, and a
    // press of a row buys or sells.
    private static float ListHeightFor(float canvasHeight, int rows, bool isTouchShown)
    {
        float needed = rows * RowHeight + Math.Max(0, rows - 1) * RowSpacing;
        float floor = Math.Max(
                Math.Max(SkillBar.Top, FeedbackLines.TopFor(canvasHeight)),
                isTouchShown ? TouchControls.StickBounds.yMax : 0f)
            + Margin;
        float room = canvasHeight - (StatusBarHeight + Margin) - Chrome - floor;
        return Math.Max(0f, Math.Min(needed, room));
    }

    private static string NpcName(ClientContent? content, string definitionId)
    {
        return content != null
            && NpcDefinitionId.TryCreate(definitionId, out NpcDefinitionId id)
            && content.TryGetNpc(id, out ClientNpc? npc)
            && npc != null
                ? npc.DisplayName
                : definitionId;
    }

    private static bool TryGetSellPrice(NpcServices shop, ItemDefinitionId item, out uint price)
    {
        foreach (NpcServiceEntry entry in shop.Entries)
        {
            if (entry.Item == item && entry.SellPrice > 0)
            {
                price = entry.SellPrice;
                return true;
            }
        }

        price = 0;
        return false;
    }

    // A long row shrinks to fit its one line, as in the inventory window.
    private static void FitOnOneLine(TMP_Text label)
    {
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = label.fontSize;
    }

    // Rebuilds the lists only when the NPC's services, the quest log, the inventory, its revision, or the names to show
    // have changed.
    private void Show(ClientWorld world, ClientContent? content)
    {
        world.TryGetNpcServices(Npc, out NpcServices? services);
        NpcServices? shop = services != null && services.Entries.Count > 0 ? services : null;
        ClientInventory inventory = world.Inventory;
        bool hasContent = content != null;
        if (services == m_shownServices
            && world.Quests == m_shownQuests
            && inventory == m_shownInventory
            && inventory.IsCurrent == m_shownCurrent
            && inventory.Revision == m_shownRevision
            && hasContent == m_hadContent)
        {
            return;
        }

        m_shownServices = services;
        m_shownQuests = world.Quests;
        m_shownInventory = inventory;
        m_shownCurrent = inventory.IsCurrent;
        m_shownRevision = inventory.Revision;
        m_hadContent = hasContent;
        ClearRows();
        m_text.Clear();
        bool hasQuests = services != null && services.Offers.Count > 0;
        bool offersReset = services != null && services.OffersReset;
        UiBuilder.SetActive(m_list!, shop != null || hasQuests || offersReset);
        m_coins!.text = shop != null && inventory.IsCurrent ? $"Coins: {inventory.Coins}" : string.Empty;
        if (shop != null)
        {
            ListForSale(shop, content);
            ListSellable(shop, inventory, content);
        }

        if (hasQuests)
        {
            ListQuests(services!, world.Quests, content);
        }

        if (offersReset)
        {
            ListReset();
        }

        Text = m_text.ToString();
        TextChanges++;
    }

    private void ListForSale(NpcServices shop, ClientContent? content)
    {
        AddHeading("Buy");
        int listed = 0;
        foreach (NpcServiceEntry entry in shop.Entries)
        {
            if (entry.BuyPrice == 0)
            {
                continue;
            }

            string text = $"{TradeMessages.ItemName(content, entry.Item)}: {TradeMessages.Coins(entry.BuyPrice)}";
            GameClient client = m_client!;
            EntityId npc = Npc;
            ItemDefinitionId item = entry.Item;
            AddButton(text, () => client.BuyFrom(npc, item, 1));
            listed++;
        }

        if (listed == 0)
        {
            AddLine("Nothing for sale");
        }
    }

    private void ListSellable(NpcServices shop, ClientInventory inventory, ClientContent? content)
    {
        AddHeading("Sell");
        if (!inventory.IsCurrent)
        {
            AddLine("Waiting for the server");
            return;
        }

        int listed = 0;
        foreach (InventoryEntry row in inventory.Rows)
        {
            if (row.Slot == EquipmentSlot.None && TryGetSellPrice(shop, row.Item, out uint price))
            {
                AddSellRow(row, price, content);
                listed++;
            }
        }

        if (listed == 0)
        {
            AddLine("Nothing to sell");
        }
    }

    // For each quest the NPC gives: its name, objective, and reward, then where the character stands with it, and the
    // one button that fits: Accept while it has no entry, and Turn in once it reaches its count.
    private void ListQuests(NpcServices services, IReadOnlyList<QuestLogEntry> log, ClientContent? content)
    {
        GameClient client = m_client!;
        EntityId npc = Npc;
        foreach (NpcQuestOffer offer in services.Offers)
        {
            QuestDefinitionId quest = offer.Quest;
            AddHeading(QuestMessages.QuestName(content, quest));
            AddLine(QuestMessages.Objective(offer, content));
            AddLine(QuestMessages.Reward(offer));
            if (!TryFind(log, quest, out QuestLogEntry entry))
            {
                AddButton("Accept", () => client.AcceptQuestFrom(npc, quest));
            }
            else if (entry.State == QuestState.Completed)
            {
                AddLine("Completed");
            }
            else
            {
                AddLine($"Progress: {entry.Progress}/{entry.Count}");
                if (entry.Progress >= entry.Count)
                {
                    AddButton("Turn in", () => client.TurnInQuestTo(npc, quest));
                }
            }
        }
    }

    // The Guildmaster's reset (Gameplay Systems §6.1): free, but it undoes every choice, so the first press only arms it.
    private void ListReset()
    {
        AddHeading("Stat and skill points");
        AddLine("Every point back, for free");
        AddButton(m_isResetArmed ? "Press again to reset" : "Reset all points", PressReset);
    }

    private void PressReset()
    {
        if (m_isResetArmed && Time.unscaledTime <= m_resetArmedUntil)
        {
            ArmReset(false);
            m_client!.ResetBuildAt(Npc);
            return;
        }

        ArmReset(true);
    }

    // The lists are written again so the button says what its next press does.
    private void ArmReset(bool isArmed)
    {
        m_isResetArmed = isArmed;
        m_resetArmedUntil = Time.unscaledTime + ResetConfirmSeconds;
        m_shownServices = null;
    }

    private static bool TryFind(IReadOnlyList<QuestLogEntry> log, QuestDefinitionId quest, out QuestLogEntry entry)
    {
        foreach (QuestLogEntry candidate in log)
        {
            if (candidate.Quest == quest)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    // One press sells one; a stack also offers All, which sells the whole row.
    private void AddSellRow(InventoryEntry row, uint price, ClientContent? content)
    {
        string name = TradeMessages.ItemName(content, row.Item);
        string text = row.Quantity > 1
            ? $"{name} x {row.Quantity}: {TradeMessages.Coins(price)} each"
            : $"{name} x 1: {TradeMessages.Coins(price)}";
        GameClient client = m_client!;
        EntityId npc = Npc;
        long inventoryItem = row.InventoryItem;
        uint quantity = row.Quantity;
        GameObject line = Ui.CreateRow($"Sell {inventoryItem}", m_rows!);
        GameObject one = CreateButton(text, line.transform, () => client.SellTo(npc, inventoryItem, 1));
        one.GetComponent<LayoutElement>().flexibleWidth = 1f;
        if (quantity > 1)
        {
            string all = $"All: {TradeMessages.Coins((long)price * quantity)}";
            GameObject allButton = CreateButton(all, line.transform, () => client.SellTo(npc, inventoryItem, quantity));
            allButton.GetComponent<LayoutElement>().preferredWidth = AllWidth;
        }

        m_rowObjects.Add(line);
        AppendText(text);
    }

    private void AddButton(string text, UnityAction onClick)
    {
        m_rowObjects.Add(CreateButton(text, m_rows!, onClick));
        AppendText(text);
    }

    private static GameObject CreateButton(string text, Transform parent, UnityAction onClick)
    {
        GameObject button = Ui.CreateButton(text, parent, onClick);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        label.alignment = TextAlignmentOptions.MidlineLeft;
        FitOnOneLine(label);
        return button;
    }

    private void AddHeading(string text)
    {
        TMP_Text label = AddLine(text);
        label.fontStyle = FontStyles.Bold;
    }

    private TMP_Text AddLine(string text)
    {
        TMP_Text label = Ui.CreateLabel("Row", m_rows!);
        label.text = text;
        FitOnOneLine(label);
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        m_rowObjects.Add(label.gameObject);
        AppendText(text);
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
            m_listLayout!.preferredHeight = height;
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

        // The coins share the name's line, so the lists keep all the room there is on a short screen.
        GameObject heading = Ui.CreateRow("Heading", panel);
        m_name = Ui.CreateLabel("Name", heading.transform);
        m_name.fontStyle = FontStyles.Bold;
        m_name.gameObject.AddComponent<LayoutElement>().preferredWidth = NameWidth;
        FitOnOneLine(m_name);
        m_coins = Ui.CreateLabel("Coins", heading.transform);
        m_coins.alignment = TextAlignmentOptions.MidlineRight;
        m_coins.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        FitOnOneLine(m_coins);
        Ui.CreateButton("Close", heading.transform, Close).GetComponent<LayoutElement>().preferredWidth = CloseWidth;
        m_rows = CreateList(panel);
        m_panel.SetActive(false);
    }

    // The rows scroll inside a list as tall as FitList allows.
    private Transform CreateList(Transform panel)
    {
        m_list = new GameObject("List", typeof(RectTransform));
        m_list.transform.SetParent(panel, false);
        m_list.AddComponent<RectMask2D>();

        // Clear, so that a drag anywhere in the list scrolls it, between the rows too.
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
