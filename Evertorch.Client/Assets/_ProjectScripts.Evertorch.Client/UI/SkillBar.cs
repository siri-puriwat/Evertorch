using System;
using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The skill bar across the bottom centre while in the world (Prototype Content §2, §4): a button for each skill
///     slot the skill list fills, with the slot's key or what is left of the skill's cooldown once the skill is learned
///     and "Locked" until then, and for each potion slot while the inventory holds its potion, with how many and the
///     key. With a first job's own skills on 6 to 8 the slots narrow so all eight fit between the stick and the touch
///     buttons, and the keys the gamepad's page reaches are shown in brackets. It is text only, and a press asks for
///     the slot exactly as its key does; a locked slot's press does nothing. The slot of a skill waiting for its target
///     is drawn in the accent colour until the target is chosen or the wait ends (Prototype Content §4).
/// </summary>
public sealed class SkillBar : MonoBehaviour
{
    /// <summary>
    ///     How far the bar sits above the bottom edge, in canvas units: level with the touch buttons.
    /// </summary>
    public const float BottomInset = 32f;

    public const float Height = SlotHeight + 2 * Padding;

    /// <summary>
    ///     The bar's top edge in canvas units, which the chat keeps clear of.
    /// </summary>
    public const float Top = BottomInset + Height;

    // Over the combat HUD and the stick, beside the status bar.
    private const int SortingOrder = 6;
    private const float SlotWidth = 112f;

    // Eight slots of 70 with the padding and the spacing make 628, within the 636 between the stick and the buttons.
    private const float NarrowSlotWidth = 70f;
    private const int NarrowSlotCount = 6;
    private const float SlotHeight = 76f;
    private const float Spacing = 8f;
    private const int Padding = 6;

    // What a locked slot records as shown, below any cooldown's tenths.
    private const int LockedTenths = -2;

    private static readonly UiBuilder Ui = new(22f, SlotHeight, 0f, Spacing);

    private readonly Slot[] m_slots = new Slot[SkillSlots.Count];
    private GameClient? m_client;
    private GameObject? m_bar;
    private float m_shownSlotWidth = SlotWidth;

    /// <summary>
    ///     How many times a slot's text was rewritten, which only a changed name, key, or tenth of a second may cause.
    /// </summary>
    public int TextChanges { get; private set; }

    public bool IsVisible => m_bar != null && m_bar.activeSelf;

    /// <summary>
    ///     The width of each slot's button, in canvas units.
    /// </summary>
    public float ShownSlotWidth => m_shownSlotWidth;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        IReadOnlyList<SkillListEntry> skills = world != null ? world.Skills : Array.Empty<SkillListEntry>();
        bool hasOwnSkills = SkillSlots.HasOwnSkills(skills);
        bool isGamepadOnOwnSkills = m_client != null && m_client.IsGamepadOnOwnSkills;
        SkillDefinitionId choosing = m_client != null ? m_client.TargetingSkill : default;
        FitSlots(hasOwnSkills ? NarrowSlotWidth : SlotWidth);
        bool isAnyShown = false;
        foreach (Slot slot in m_slots)
        {
            bool isShown;
            if (slot.IsPotion)
            {
                int held = world != null ? InventoryActions.CountOf(world.Inventory.Rows, slot.Item) : 0;
                isShown = held > 0;
                if (isShown)
                {
                    ShowPotion(slot, held);
                }
            }
            else
            {
                SkillDefinitionId skill = default;
                isShown = world != null && SkillSlots.TryGetSkill(skills, slot.Number, out skill);
                if (isShown)
                {
                    Assign(slot, skill);
                    bool isOnPage = hasOwnSkills
                        && (isGamepadOnOwnSkills
                            ? slot.Number >= SkillSlots.FirstOwnSlot
                            : slot.Number < SkillSlots.FirstOwnSlot);
                    Show(slot, world!.SkillLevel(slot.Skill) > 0, world.CooldownRemaining(slot.Skill), isOnPage);
                    ShowChoosing(slot, choosing != default && slot.Skill == choosing);
                }
            }

            UiBuilder.SetActive(slot.Button, isShown);
            isAnyShown |= isShown;
        }

        UiBuilder.SetActive(m_bar!, isAnyShown);
    }

    public static SkillBar Create(GameClient client)
    {
        var root = new GameObject("SkillBar", typeof(RectTransform));
        root.SetActive(false);
        SkillBar bar = root.AddComponent<SkillBar>();
        bar.Build(client);
        root.SetActive(true);
        return bar;
    }

    /// <summary>
    ///     The bar's width in canvas units while <paramref name="shownSlots" /> slots show: a slot of 6 to 8 narrows them
    ///     all.
    /// </summary>
    public static float WidthFor(int shownSlots)
    {
        float slotWidth = shownSlots >= NarrowSlotCount ? NarrowSlotWidth : SlotWidth;
        return 2 * Padding + shownSlots * slotWidth + Math.Max(0, shownSlots - 1) * Spacing;
    }

    /// <summary>
    ///     Where the bar is on a canvas <paramref name="canvasWidth" /> units wide, from its bottom-left corner.
    /// </summary>
    public static Rect BoundsFor(float canvasWidth, int shownSlots)
    {
        float width = WidthFor(shownSlots);
        return new Rect((canvasWidth - width) / 2f, BottomInset, width, Height);
    }

    private static string ItemName(ClientContent? content, ItemDefinitionId item)
    {
        return content != null && content.TryGetItem(item, out ClientItem? found) && found != null
            ? found.DisplayName
            : item.Value;
    }

    private void ShowPotion(Slot slot, int held)
    {
        string name = ItemName(m_client != null ? m_client.Content : null, slot.Item);
        if (held == slot.ShownTenths && string.Equals(name, slot.ShownName, StringComparison.Ordinal))
        {
            return;
        }

        slot.ShownTenths = held;
        slot.ShownName = name;
        slot.Label.text = $"{name} x {held}\n{slot.Number.ToString(CultureInfo.InvariantCulture)}";
        TextChanges++;
    }

    // A slot's skill follows the skill list; a new one is written out afresh.
    private static void Assign(Slot slot, SkillDefinitionId skill)
    {
        if (slot.Skill != skill)
        {
            slot.Skill = skill;
            slot.ShownName = null;
        }
    }

    // Only the button's colour changes, so the text and its count stay as they were.
    private static void ShowChoosing(Slot slot, bool isChoosing)
    {
        if (isChoosing == slot.IsShownChoosing)
        {
            return;
        }

        slot.IsShownChoosing = isChoosing;
        slot.Background.color = isChoosing ? UiBuilder.AccentColor : UiBuilder.ControlColor;
    }

    private void FitSlots(float width)
    {
        if (width == m_shownSlotWidth)
        {
            return;
        }

        m_shownSlotWidth = width;
        foreach (Slot slot in m_slots)
        {
            slot.Button.GetComponent<LayoutElement>().preferredWidth = width;
        }
    }

    // Tenths of a second, rounded up, so a cooldown never reads 0.0 while it lasts.
    private void Show(Slot slot, bool isLearned, double cooldownSeconds, bool isOnPage)
    {
        int tenths = isLearned ? (int)Math.Ceiling(cooldownSeconds * 10.0) : LockedTenths;
        string name = BuildMessages.SkillName(m_client != null ? m_client.Content : null, slot.Skill);
        if (tenths == slot.ShownTenths
            && isOnPage == slot.IsShownOnPage
            && string.Equals(name, slot.ShownName, StringComparison.Ordinal))
        {
            return;
        }

        slot.ShownTenths = tenths;
        slot.ShownName = name;
        slot.IsShownOnPage = isOnPage;
        string key = slot.Number.ToString(CultureInfo.InvariantCulture);
        string detail = !isLearned
            ? "Locked"
            : tenths > 0
                ? (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"
                : isOnPage
                    ? $"[{key}]"
                    : key;
        slot.Label.text = $"{name}\n{detail}";
        slot.Button.GetComponent<Button>().interactable = isLearned;
        TextChanges++;
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        m_bar = UiBuilder.CreateUiObject("Bar", transform);
        var rect = (RectTransform)m_bar.transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, BottomInset);
        rect.sizeDelta = new Vector2(0f, Height);
        m_bar.AddComponent<Image>().color = UiBuilder.PanelColor;
        HorizontalLayoutGroup layout = m_bar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(Padding, Padding, Padding, Padding);
        layout.spacing = Spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        m_bar.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        for (int index = 0; index < m_slots.Length; index++)
        {
            int number = index + 1;
            SkillSlots.TryGetItem(number, out ItemDefinitionId item);
            GameObject button = Ui.CreateButton($"Slot {number}", m_bar.transform, () => UseSlot(number));
            button.GetComponent<LayoutElement>().preferredWidth = SlotWidth;
            button.SetActive(false);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

            // A potion's name is longer than a slot is wide, so a label shrinks to fit rather than overflow.
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = label.fontSize;
            m_slots[index] = new Slot(number, item, button, button.GetComponent<Image>(), label);
        }

        m_bar.SetActive(false);
    }

    private void UseSlot(int number)
    {
        if (m_client != null)
        {
            m_client.UseSkillSlot(number);
        }
    }

    private sealed class Slot
    {
        public Slot(int number, ItemDefinitionId item, GameObject button, Image background, TMP_Text label)
        {
            Number = number;
            Item = item;
            Button = button;
            Background = background;
            Label = label;
        }

        public int Number { get; }

        /// <summary>The skill the skill list puts in the slot; default for a potion's slot.</summary>
        public SkillDefinitionId Skill { get; set; }

        /// <summary>The slot's potion; default for a skill's slot.</summary>
        public ItemDefinitionId Item { get; }

        public bool IsPotion => Item != default;

        public GameObject Button { get; }

        public Image Background { get; }

        public TMP_Text Label { get; }

        /// <summary>The tenths of a second of cooldown shown, or for a potion the count.</summary>
        public int ShownTenths { get; set; } = -1;

        public string? ShownName { get; set; }

        /// <summary>Whether the key shown is one the gamepad's page reaches.</summary>
        public bool IsShownOnPage { get; set; }

        /// <summary>Whether the slot is drawn as the skill waiting for its target.</summary>
        public bool IsShownChoosing { get; set; }
    }
}
}
