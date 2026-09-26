using System;
using System.Globalization;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The skill bar across the bottom centre while in the world (Prototype Content §2, §4): a button for each slot
///     whose skill the server listed, with the slot's key or what is left of the skill's cooldown, and for each potion
///     slot while the inventory holds its potion, with how many and the key. It is text only, and a press asks for the
///     slot exactly as its key does.
/// </summary>
public sealed class SkillBar : MonoBehaviour
{
    /// <summary>
    ///     How far the bar sits above the bottom edge, in canvas units: level with the touch buttons.
    /// </summary>
    public const float BottomInset = 32f;

    public const float Height = SlotHeight + 2 * Padding;

    /// <summary>
    ///     The bar's top edge in canvas units, which the feedback lines keep clear of.
    /// </summary>
    public const float Top = BottomInset + Height;

    // Over the combat HUD and the stick, beside the status bar.
    private const int SortingOrder = 6;
    private const float SlotWidth = 112f;
    private const float SlotHeight = 76f;
    private const float Spacing = 8f;
    private const int Padding = 6;

    private static readonly UiBuilder Ui = new(22f, SlotHeight, 0f, Spacing);

    private readonly Slot[] m_slots = new Slot[SkillSlots.Count];
    private GameClient? m_client;
    private GameObject? m_bar;

    /// <summary>
    ///     How many times a slot's text was rewritten, which only a changed name, key, or tenth of a second may cause.
    /// </summary>
    public int TextChanges { get; private set; }

    public bool IsVisible => m_bar != null && m_bar.activeSelf;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
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
                isShown = world != null && IsListed(world, slot.Skill);
                if (isShown)
                {
                    Show(slot, world!.CooldownRemaining(slot.Skill));
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
    ///     The bar's width in canvas units while <paramref name="shownSlots" /> slots show.
    /// </summary>
    public static float WidthFor(int shownSlots)
    {
        return 2 * Padding + shownSlots * SlotWidth + Math.Max(0, shownSlots - 1) * Spacing;
    }

    /// <summary>
    ///     Where the bar is on a canvas <paramref name="canvasWidth" /> units wide, from its bottom-left corner.
    /// </summary>
    public static Rect BoundsFor(float canvasWidth, int shownSlots)
    {
        float width = WidthFor(shownSlots);
        return new Rect((canvasWidth - width) / 2f, BottomInset, width, Height);
    }

    private static bool IsListed(ClientWorld world, SkillDefinitionId skill)
    {
        foreach (SkillListEntry entry in world.Skills)
        {
            if (entry.Skill == skill)
            {
                return true;
            }
        }

        return false;
    }

    private static string SkillName(ClientContent? content, SkillDefinitionId skill)
    {
        return content != null && content.TryGetSkill(skill, out ClientSkill? found) && found != null
            ? found.DisplayName
            : skill.Value;
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

    // Tenths of a second, rounded up, so a cooldown never reads 0.0 while it lasts.
    private void Show(Slot slot, double cooldownSeconds)
    {
        int tenths = (int)Math.Ceiling(cooldownSeconds * 10.0);
        string name = SkillName(m_client != null ? m_client.Content : null, slot.Skill);
        if (tenths == slot.ShownTenths && string.Equals(name, slot.ShownName, StringComparison.Ordinal))
        {
            return;
        }

        slot.ShownTenths = tenths;
        slot.ShownName = name;
        string detail = tenths > 0
            ? (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : slot.Number.ToString(CultureInfo.InvariantCulture);
        slot.Label.text = $"{name}\n{detail}";
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
            SkillSlots.TryGetSkill(number, out SkillDefinitionId skill);
            SkillSlots.TryGetItem(number, out ItemDefinitionId item);
            GameObject button = Ui.CreateButton($"Slot {number}", m_bar.transform, () => UseSlot(number));
            button.GetComponent<LayoutElement>().preferredWidth = SlotWidth;
            button.SetActive(false);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

            // A potion's name is longer than a slot is wide, so a label shrinks to fit rather than overflow.
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = label.fontSize;
            m_slots[index] = new Slot(number, skill, item, button, label);
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
        public Slot(int number, SkillDefinitionId skill, ItemDefinitionId item, GameObject button, TMP_Text label)
        {
            Number = number;
            Skill = skill;
            Item = item;
            Button = button;
            Label = label;
        }

        public int Number { get; }

        public SkillDefinitionId Skill { get; }

        /// <summary>The slot's potion; default for a skill's slot.</summary>
        public ItemDefinitionId Item { get; }

        public bool IsPotion => Item != default;

        public GameObject Button { get; }

        public TMP_Text Label { get; }

        /// <summary>The tenths of a second of cooldown shown, or for a potion the count.</summary>
        public int ShownTenths { get; set; } = -1;

        public string? ShownName { get; set; }
    }
}
}
