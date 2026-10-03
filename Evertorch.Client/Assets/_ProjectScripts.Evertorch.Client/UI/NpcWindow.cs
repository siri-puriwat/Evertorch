using System;
using System.Collections.Generic;
using System.Globalization;
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
///     reward, and where the character stands with it; and for the Guildmaster, "Reset all points" and each first job
///     it offers from the character's job, "Become a Vanguard" from its base job's cap and "Needs Adventurer Lv 10 to
///     become a Vanguard" below it; for the Storekeeper, the fee, the storage's rows, the bag's unworn rows, and an
///     amount field, where a press on a stored row withdraws the amount and one on a bag row deposits it (Gameplay
///     Systems §11.4). A Buy press buys one; a Sell press sells one, and a stack's All sells the row; Accept
///     and Turn in ask for the quest; the reset and a change each ask for a second press within
///     <see cref="ResetConfirmSeconds" />, since the reset undoes every choice and the change is final, and arming one
///     disarms the other. The presses go through <see cref="GameClient" />, and only what the server commits moves the
///     lists. It closes once the NPC is drawn beyond its range, the player dies, the map changes, or the connection
///     closes.
/// </summary>
public sealed class NpcWindow : MonoBehaviour
{
    /// <summary>
    ///     How long "Press again to reset" and "Press again to become a Vanguard" wait for the second press.
    /// </summary>
    public const float ResetConfirmSeconds = 5f;

    // With the status bar and the target frame, under the chat and the login panel.
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

    public const string AmountField = "Amount";

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
    private float m_left = Margin;
    private ClientWorld? m_world;
    private NpcServices? m_shownServices;
    private IReadOnlyList<QuestLogEntry>? m_shownQuests;
    private ClientInventory? m_shownInventory;
    private bool m_shownCurrent;
    private uint m_shownRevision;
    private bool m_hadContent;
    private bool m_isResetArmed;
    private float m_resetArmedUntil;
    private JobDefinitionId m_armedJob;
    private JobDefinitionId m_shownJob;
    private int m_shownJobLevel = -1;
    private int m_shownStorage = -1;
    private TMP_InputField? m_amount;
    private GameObject? m_amountRow;
    private bool m_needsRead;

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

    /// <summary>
    ///     The amount field's number: how many of a row a press stores or takes back; at least 1.
    /// </summary>
    public uint Amount =>
        m_amount != null
        && uint.TryParse(m_amount.text, NumberStyles.None, CultureInfo.InvariantCulture, out uint amount)
        && amount > 0
            ? amount
            : 1;

    public TMP_InputField? AmountInput => m_amount;

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

        if ((m_isResetArmed || m_armedJob != default) && Time.unscaledTime > m_resetArmedUntil)
        {
            ArmReset(false);
        }

        Show(world, m_client.Content);
        FitList();
        ReadStorageWhenDue(world);
    }

    // Storage is read on opening and again whenever what is shown may be out of date; GameClient sends one read at a
    // time, within its own bucket.
    private void ReadStorageWhenDue(ClientWorld world)
    {
        if (!world.TryGetNpcServices(Npc, out NpcServices? services) || services == null || !services.KeepsStorage)
        {
            return;
        }

        ClientStorage storage = world.Storage;
        if ((m_needsRead || !storage.IsCurrent) && !storage.IsReading && m_client!.OpenStorageAt(Npc) != 0)
        {
            m_needsRead = false;
        }
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
    ///     down to the top of the chat, the skill bar, or the stick while the touch controls show, whichever is
    ///     highest. Rows beyond that scroll.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, int rows, bool isTouchShown)
    {
        float top = canvasHeight - (StatusBarHeight + Margin);
        float height = Chrome + ListHeightFor(canvasHeight, rows, isTouchShown);
        return new Rect(LeftFor(isTouchShown), top - height, Width, height);
    }

    /// <summary>
    ///     The left edge of the windows at the top left, this one, the Stats window, and the Skills window, in canvas
    ///     units: beside the Dev, Stats, and Skills buttons while the touch controls show, so those stay pressable
    ///     (finding C2 of the Milestone 9 review).
    /// </summary>
    public static float LeftFor(bool isTouchShown)
    {
        return isTouchShown ? TouchControls.WindowButtonsRight + Margin : Margin;
    }

    /// <summary>
    ///     The right edge of the windows at the top left, in canvas units.
    /// </summary>
    public static float RightFor(bool isTouchShown)
    {
        return LeftFor(isTouchShown) + Width;
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
        m_needsRead = true;
        m_name!.text = NpcName(m_client!.Content, remote.DefinitionId);
        UiBuilder.SetActive(m_panel!, true);
        Show(world, m_client.Content);
    }

    public void Close()
    {
        m_world = null;
        Npc = default;
        m_isResetArmed = false;
        m_armedJob = default;

        // The services stay cached for the NPC, so the next open must write the lists again, disarmed.
        m_shownServices = null;
        m_needsRead = false;

        // A field that had focus when the window closed gives the keys back.
        if (m_amount != null)
        {
            m_client?.SetTyping(m_amount, false);
        }

        UiBuilder.SetActive(m_panel!, false);
    }

    // A row over a skill slot, the stick, or the chat would hide them or take the presses meant for them, and a
    // press of a row buys or sells.
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
            && world.LocalJob == m_shownJob
            && (world.Sheet?.JobLevel ?? -1) == m_shownJobLevel
            && world.Quests == m_shownQuests
            && inventory == m_shownInventory
            && inventory.IsCurrent == m_shownCurrent
            && inventory.Revision == m_shownRevision
            && world.Storage.Version == m_shownStorage
            && hasContent == m_hadContent)
        {
            return;
        }

        m_shownServices = services;
        m_shownJob = world.LocalJob;
        m_shownJobLevel = world.Sheet?.JobLevel ?? -1;
        m_shownQuests = world.Quests;
        m_shownInventory = inventory;
        m_shownCurrent = inventory.IsCurrent;
        m_shownRevision = inventory.Revision;
        m_shownStorage = world.Storage.Version;
        m_hadContent = hasContent;
        ClearRows();
        m_text.Clear();
        bool hasQuests = services != null && services.Offers.Count > 0;
        bool offersReset = services != null && services.OffersReset;
        bool offersChange = services != null && HasChangeFrom(services, world.LocalJob);
        bool keepsStorage = services != null && services.KeepsStorage;
        UiBuilder.SetActive(m_list!, shop != null || hasQuests || offersReset || offersChange || keepsStorage);
        UiBuilder.SetActive(m_amountRow!, keepsStorage);
        m_coins!.text = (shop != null || keepsStorage) && inventory.IsCurrent
            ? $"Coins: {inventory.Coins}"
            : string.Empty;
        if (keepsStorage)
        {
            ListStorage(world.Storage, inventory, content);
        }

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

        if (offersChange)
        {
            ListJobChanges(services!, world, content);
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

            string text = $"{ShopMessages.ItemName(content, entry.Item)}: {ShopMessages.Coins(entry.BuyPrice)}";
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

    // The Storekeeper's fee, then each stored row, whose press takes the amount back, and each unworn bag row, whose
    // press stores it; a row holding less gives what it holds.
    private void ListStorage(ClientStorage storage, ClientInventory inventory, ClientContent? content)
    {
        AddHeading("Storage");
        if (!storage.IsCurrent)
        {
            AddLine("Waiting for the server");
            return;
        }

        AddLine(StorageMessages.Fee(storage.DepositFee));
        GameClient client = m_client!;
        EntityId npc = Npc;
        foreach (StorageEntry row in storage.Rows)
        {
            long storageItem = row.StorageItem;
            uint held = row.Quantity;
            AddButton(
                RowText(content, row.Item, row.Quantity, row.RefineLevel),
                () => client.WithdrawAt(npc, storageItem, Math.Min(Amount, held)));
        }

        if (storage.Rows.Count == 0)
        {
            AddLine("Nothing stored");
        }

        AddHeading("Bag");
        if (!inventory.IsCurrent)
        {
            AddLine("Waiting for the server");
            return;
        }

        int listed = 0;
        foreach (InventoryEntry row in inventory.Rows)
        {
            if (row.Slot != EquipmentSlot.None)
            {
                continue;
            }

            long inventoryItem = row.InventoryItem;
            uint held = row.Quantity;
            AddButton(
                RowText(content, row.Item, row.Quantity, row.RefineLevel),
                () => client.DepositAt(npc, inventoryItem, Math.Min(Amount, held)));
            listed++;
        }

        if (listed == 0)
        {
            AddLine("Nothing to store");
        }
    }

    private static string RowText(ClientContent? content, ItemDefinitionId item, uint quantity, byte refineLevel)
    {
        string name = ShopMessages.ItemName(content, item);
        string refined = refineLevel > 0 ? $"+{refineLevel} {name}" : name;
        return quantity == 1 ? refined : $"{refined} x {quantity}";
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

    // The lists are written again so the button says what its next press does; arming the reset disarms a change.
    private void ArmReset(bool isArmed)
    {
        m_isResetArmed = isArmed;
        m_armedJob = default;
        m_resetArmedUntil = Time.unscaledTime + ResetConfirmSeconds;
        m_shownServices = null;
    }

    private static bool HasChangeFrom(NpcServices services, JobDefinitionId job)
    {
        foreach (NpcJobChangeOffer change in services.JobChanges)
        {
            if (change.FromJob == job)
            {
                return true;
            }
        }

        return false;
    }

    // The Guildmaster's job changes (Gameplay Systems §6.1) from the character's job: final, so the first press only
    // arms one; below the base job's cap, what it still needs.
    private void ListJobChanges(NpcServices services, ClientWorld world, ClientContent? content)
    {
        AddHeading("Change job");
        foreach (NpcJobChangeOffer change in services.JobChanges)
        {
            if (change.FromJob != world.LocalJob)
            {
                continue;
            }

            string name = BuildMessages.JobName(content, change.Job);
            if (world.Sheet == null || world.Sheet.JobLevel < change.Level)
            {
                AddLine(
                    $"Needs {BuildMessages.JobName(content, change.FromJob)} Lv {change.Level} to become "
                    + BuildMessages.WithArticle(name));
                continue;
            }

            JobDefinitionId job = change.Job;
            AddButton(
                m_armedJob == job
                    ? $"Press again to become {BuildMessages.WithArticle(name)}"
                    : $"Become {BuildMessages.WithArticle(name)}",
                () => PressChange(job));
        }
    }

    private void PressChange(JobDefinitionId job)
    {
        if (m_armedJob == job && Time.unscaledTime <= m_resetArmedUntil)
        {
            ArmReset(false);
            m_client!.ChangeJobAt(Npc, job);
            return;
        }

        ArmReset(false);
        m_armedJob = job;
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
        string name = ShopMessages.ItemName(content, row.Item);
        string text = row.Quantity > 1
            ? $"{name} x {row.Quantity}: {ShopMessages.Coins(price)} each"
            : $"{name} x 1: {ShopMessages.Coins(price)}";
        GameClient client = m_client!;
        EntityId npc = Npc;
        long inventoryItem = row.InventoryItem;
        uint quantity = row.Quantity;
        GameObject line = Ui.CreateRow($"Sell {inventoryItem}", m_rows!);
        GameObject one = CreateButton(text, line.transform, () => client.SellTo(npc, inventoryItem, 1));
        one.GetComponent<LayoutElement>().flexibleWidth = 1f;
        if (quantity > 1)
        {
            string all = $"All: {ShopMessages.Coins((long)price * quantity)}";
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
        PlaceLeft(isTouchShown);

        // The amount field takes a row of the room the list would have.
        float field = m_amountRow!.activeSelf ? RowHeight + RowSpacing : 0f;
        float height = Math.Max(0f, ListHeightFor(canvasHeight, m_rowObjects.Count, isTouchShown) - field);
        if (height != m_listHeight)
        {
            m_listHeight = height;
            m_listLayout!.preferredHeight = height;
        }
    }

    private void PlaceLeft(bool isTouchShown)
    {
        float left = LeftFor(isTouchShown);
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

        // While the field has focus the gameplay keys are shut, as the chat's are (Prototype Content §4).
        TMP_InputField amount = Ui.CreateField(panel, AmountField, "1", TMP_InputField.ContentType.IntegerNumber);
        amount.characterLimit = 7;
        amount.onSelect.AddListener(_ => client.SetTyping(amount, true));
        amount.onDeselect.AddListener(_ => client.SetTyping(amount, false));
        m_amount = amount;
        m_amountRow = amount.transform.parent.gameObject;
        m_amountRow.SetActive(false);
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
