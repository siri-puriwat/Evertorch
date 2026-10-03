using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The chat log at the bottom left, with its input (Prototype Content §2, §4; owner decision 13): the newest lines
///     of the client's <see cref="ChatLog" />, which also carries, as grey system lines, what the feedback lines used to
///     say: a refused command in plain words, what the player picked up, bought, or sold, a quest's news, a level-up, a
///     job level-up, a raised statistic, and a learned skill level. While a skill waits for its target, its prompt stays
///     as the last line. Enter opens the input and sends; Esc closes it. Rich text is off everywhere.
/// </summary>
public sealed class ChatPanel : MonoBehaviour
{
    public const int DesktopLines = 4;
    public const int TouchLines = 3;

    // Over the other in-world panels, under the login panel.
    private const int SortingOrder = 7;
    private const float Padding = 4f;
    private const float InputHeight = 26f;
    private const float DesktopFontSize = 18f;
    private const float TouchFontSize = 16f;

    /// <summary>
    ///     The chat on the desktop in canvas units from the bottom left: x 8 to 540, y 128 to 262, four lines and the
    ///     input.
    /// </summary>
    public static readonly Rect DesktopBounds = new(8f, 128f, 532f, 134f);

    /// <summary>
    ///     The log with the touch controls: x 226 to 762, y 128 to 200, three lines, the input over it while typing.
    /// </summary>
    public static readonly Rect TouchBounds = new(226f, 128f, 536f, 72f);

    private static readonly Color SystemColor = new(0.62f, 0.64f, 0.68f);
    private static readonly Color PartyColor = new(0.55f, 0.92f, 0.55f);
    private static readonly Color WhisperColor = new(1f, 0.85f, 0.45f);

    private readonly List<TMP_Text> m_labels = new();
    private readonly StringBuilder m_text = new();
    private GameClient? m_client;
    private ChatLog? m_log;
    private ClientWorld? m_watched;
    private IReadOnlyList<QuestLogEntry>? m_lastQuests;
    private IReadOnlyList<SkillListEntry>? m_lastSkills;
    private GameObject? m_panel;
    private RectTransform? m_panelRect;
    private TMP_InputField? m_input;
    private bool m_isLogChanged = true;
    private int m_closedAtFrame = -1;
    private bool? m_isTouchLayout;
    private string? m_prompt;

    public int TextChanges { get; private set; }

    /// <summary>
    ///     The lines shown, oldest first, one per row.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    public bool IsTyping { get; private set; }

    public TMP_InputField? Input => m_input;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world != m_watched)
        {
            Watch(world);
        }

        bool isTouch = m_client != null && m_client.Touch != null && m_client.Touch.IsVisible;
        if (isTouch != m_isTouchLayout)
        {
            Place(isTouch);
        }

        ReadKeys(world);
        string? prompt = CurrentPrompt();
        if (m_isLogChanged || !string.Equals(prompt, m_prompt, StringComparison.Ordinal))
        {
            m_isLogChanged = false;
            m_prompt = prompt;
            Rewrite();
        }
    }

    private void OnDestroy()
    {
        Watch(null);
        if (m_log != null)
        {
            m_log.Changed -= OnLogChanged;
        }
    }

    public static ChatPanel Create(GameClient client)
    {
        var root = new GameObject("ChatPanel", typeof(RectTransform));
        root.SetActive(false);
        ChatPanel panel = root.AddComponent<ChatPanel>();
        panel.Build(client);
        root.SetActive(true);
        return panel;
    }

    public static Rect BoundsFor(bool isTouchShown)
    {
        return isTouchShown ? TouchBounds : DesktopBounds;
    }

    /// <summary>
    ///     How high the side windows' floor is, which they stop above: the chat's top on the desktop, and with the
    ///     touch controls where the feedback lines' top was (Prototype Content §2, "as now").
    /// </summary>
    public static float TopFor(float canvasHeight, bool isTouchShown)
    {
        if (!isTouchShown)
        {
            return DesktopBounds.yMax;
        }

        const float legacyLinesHeight = 124f;
        return Math.Max(0.2f * canvasHeight, SkillBar.Top + 16f) + legacyLinesHeight;
    }

    /// <summary>
    ///     The line that stays while a skill waits for its target (Prototype Content §4), in the words of the device that
    ///     chooses: a click and Esc, or a tap and the Clear button.
    /// </summary>
    public static string PromptFor(string skillName, SkillTargetType targetType, bool isTouch)
    {
        string target = targetType == SkillTargetType.Ally ? "a player or yourself" : "a target";
        return isTouch
            ? $"{skillName}: tap {target}. Clear cancels."
            : $"{skillName}: click {target}. Esc cancels.";
    }

    /// <summary>
    ///     A grey system line in the log.
    /// </summary>
    public void Add(string line)
    {
        m_log?.AddSystem(line);
    }

    /// <summary>
    ///     Opens the input, which shuts the gameplay keys until it closes.
    /// </summary>
    public void Open()
    {
        if (IsTyping || m_input == null)
        {
            return;
        }

        IsTyping = true;
        m_input.text = string.Empty;
        UiBuilder.SetActive(m_input.gameObject, true);
        UiBuilder.SetActive(m_panel!, true);
        EventSystem? events = EventSystem.current;
        if (events != null)
        {
            events.SetSelectedGameObject(m_input.gameObject);
        }

        m_input.ActivateInputField();
        m_client?.SetTyping(true);
    }

    public void Close()
    {
        if (!IsTyping || m_input == null)
        {
            return;
        }

        IsTyping = false;
        m_closedAtFrame = Time.frameCount;
        m_input.DeactivateInputField();
        m_input.text = string.Empty;
        UiBuilder.SetActive(m_input.gameObject, false);
        EventSystem? events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject == m_input.gameObject)
        {
            events.SetSelectedGameObject(null);
        }

        m_client?.SetTyping(false);
        m_isLogChanged = true;
    }

    // The keys are read here rather than through the field, since the field only hears what the player types.
    private void ReadKeys(ClientWorld? world)
    {
        Keyboard? keyboard = Keyboard.current;
        bool isEnter = keyboard != null
            && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        bool isEscape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
        HandleKeys(world, isEnter, isEscape);
    }

    /// <summary>
    ///     Enter opens the input and sends; the Enter that sends does not open it again, even when the field heard it
    ///     too. Esc closes it, and so does leaving the world.
    /// </summary>
    public void HandleKeys(ClientWorld? world, bool isEnter, bool isEscape)
    {
        if (IsTyping)
        {
            if (world == null || isEscape)
            {
                Close();
            }
            else if (isEnter)
            {
                Submit();
            }
        }
        else if (isEnter
                 && world != null
                 && Time.frameCount != m_closedAtFrame
                 && (m_client == null || !m_client.IsTyping))
        {
            // An Enter typed into another field, the trade's amount say, is that field's own (Prototype Content §4).
            Open();
        }
    }

    /// <summary>
    ///     The words for a chat line the server refused: a whisper that found no one, party chat without a party, or
    ///     else the refusal's own words.
    /// </summary>
    public static string DescribeRefusal(ChatChannel channel, string recipient, CommandRejectionReason reason)
    {
        if (channel == ChatChannel.Whisper && reason == CommandRejectionReason.InvalidTarget)
        {
            return $"{recipient} is not online.";
        }

        if (channel == ChatChannel.Party && reason == CommandRejectionReason.NotAllowedNow)
        {
            return "You are not in a party.";
        }

        return RejectionMessages.Describe(reason);
    }

    private void Submit()
    {
        if (!IsTyping)
        {
            return;
        }

        string typed = m_input != null ? m_input.text : string.Empty;
        Close();
        string? own = m_client != null ? m_client.PlayedCharacter?.Name : null;
        ChatRequest request = ChatCommands.Parse(typed, m_log?.LastWhisperer, own);
        if (request.Refusal != null)
        {
            Add(request.Refusal);
        }
        else if (request.Trader != null)
        {
            m_client?.RequestTrade(request.Trader);
        }
        else if (request.Invitee != null)
        {
            m_client?.InviteToParty(request.Invitee);
        }
        else if (request.Channel != ChatChannel.None)
        {
            m_client?.Say(request.Channel, request.Recipient, request.Text);
        }
    }

    // At once, as the feedback lines did: a line is on screen in the frame it was told.
    private void OnLogChanged()
    {
        Rewrite();
    }

    // Watches the world for what the log tells the player itself; the log keeps what it already said.
    private void Watch(ClientWorld? world)
    {
        if (m_watched != null)
        {
            m_watched.CommandRejectedReceived -= OnRejected;
            m_watched.ItemPickedUpReceived -= OnPickedUp;
            m_watched.LeveledUp -= OnLeveledUp;
            m_watched.SheetChanged -= OnSheetChanged;
            m_watched.SkillsChanged -= OnSkillsChanged;
            m_watched.Inventory.ChangeApplied -= OnChangeApplied;
            m_watched.QuestsChanged -= OnQuestsChanged;
        }

        m_watched = world;

        // A world's first quest log is its baseline, which is no news; it may have come before this frame.
        m_lastQuests = world != null && world.QuestLogsReceived > 0 ? world.Quests : null;
        m_lastSkills = world != null && world.SkillsReceivedAt > 0 ? world.Skills : null;
        if (world != null)
        {
            world.CommandRejectedReceived += OnRejected;
            world.ItemPickedUpReceived += OnPickedUp;
            world.LeveledUp += OnLeveledUp;
            world.SheetChanged += OnSheetChanged;
            world.SkillsChanged += OnSkillsChanged;
            world.Inventory.ChangeApplied += OnChangeApplied;
            world.QuestsChanged += OnQuestsChanged;
        }
        else
        {
            Close();
        }
    }

    private void OnRejected(CommandRejected rejected)
    {
        if (m_client != null
            && m_client.Connection != null
            && m_client.Connection.TryGetChatSequence(
                rejected.CommandSequence,
                out ChatChannel channel,
                out string recipient))
        {
            Add(DescribeRefusal(channel, recipient, rejected.Reason));
            return;
        }

        if (m_client != null
            && m_client.Connection != null
            && m_client.Connection.TryGetPartySequence(
                rejected.CommandSequence,
                out PartyCommand command,
                out string name))
        {
            Add(RejectionMessages.DescribeParty(command, name, rejected.Reason));
            return;
        }

        if (m_client != null
            && m_client.Connection != null
            && m_client.Connection.TryGetTradeSequence(
                rejected.CommandSequence,
                out TradeCommand trade,
                out string partner))
        {
            Add(TradeMessages.DescribeRefusal(trade, partner, rejected.Reason));
            return;
        }

        bool isEquip = m_client != null
            && m_client.Connection != null
            && m_client.Connection.IsEquipSequence(rejected.CommandSequence);
        Add(RejectionMessages.Describe(rejected.Reason, isEquip));
    }

    private void OnPickedUp(ItemPickedUp pickedUp)
    {
        if (m_watched != null && pickedUp.Recipient == m_watched.LocalEntity)
        {
            ClientContent? content = m_client != null ? m_client.Content : null;
            Add($"Picked up {ShopMessages.ItemName(content, pickedUp.Item)} x {pickedUp.Amount}");
        }
    }

    private void OnLeveledUp()
    {
        Add("Level up");
    }

    // A world's first skill list is its baseline, which is no news.
    private void OnSkillsChanged()
    {
        if (m_watched == null)
        {
            return;
        }

        IReadOnlyList<SkillListEntry> after = m_watched.Skills;
        foreach (string line in BuildMessages.DescribeSkills(m_lastSkills, after,
                     m_client != null ? m_client.Content : null))
        {
            Add(line);
        }

        m_lastSkills = after;
    }

    private void OnSheetChanged(CharacterSheet? before)
    {
        CharacterSheet? after = m_watched?.Sheet;
        if (after == null)
        {
            return;
        }

        foreach (string line in BuildMessages.Describe(before, after, m_client != null ? m_client.Content : null))
        {
            Add(line);
        }
    }

    // Each quest the new log changed says so; the objective's monster and the reward come from the offer this session
    // saw.
    private void OnQuestsChanged()
    {
        IReadOnlyList<QuestLogEntry> quests = m_watched!.Quests;
        IReadOnlyList<QuestLogEntry>? before = m_lastQuests;
        m_lastQuests = quests;
        if (before == null)
        {
            return;
        }

        ClientConnection? connection = m_client != null ? m_client.Connection : null;
        ClientContent? content = m_client != null ? m_client.Content : null;
        foreach (QuestLogEntry after in quests)
        {
            NpcQuestOffer? offer =
                connection != null && connection.TryGetQuestOffer(after.Quest, out NpcQuestOffer seen)
                    ? seen
                    : null;
            string? line = QuestMessages.Describe(Find(before, after.Quest), after, offer, content);
            if (line != null)
            {
                Add(line);
            }
        }
    }

    private static QuestLogEntry? Find(IReadOnlyList<QuestLogEntry> log, QuestDefinitionId quest)
    {
        foreach (QuestLogEntry entry in log)
        {
            if (entry.Quest == quest)
            {
                return entry;
            }
        }

        return null;
    }

    private void OnChangeApplied(InventoryDelta delta)
    {
        string? line = ShopMessages.Describe(delta, m_client != null ? m_client.Content : null);
        if (line != null)
        {
            Add(line);
        }
    }

    private void Build(GameClient client)
    {
        m_client = client;
        m_log = client.ChatLog;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        GameObject panel = UiBuilder.CreateUiObject("Panel", transform);
        m_panel = panel;
        m_panelRect = (RectTransform)panel.transform;
        m_panelRect.anchorMin = Vector2.zero;
        m_panelRect.anchorMax = Vector2.zero;
        m_panelRect.pivot = Vector2.zero;
        Image background = panel.AddComponent<Image>();
        background.color = UiBuilder.PanelColor;

        // The log only speaks: a click on a monster behind it, a skill's target among its lines, reaches the world.
        background.raycastTarget = false;
        var ui = new UiBuilder(DesktopFontSize, InputHeight, 0f, 0f);
        for (int index = 0; index < DesktopLines; index++)
        {
            TMP_Text label = ui.CreateLabel($"Line{index + 1}", panel.transform);
            label.richText = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            m_labels.Add(label);
        }

        m_input = CreateInput(ui, panel.transform);

        // A touch keyboard's Done reaches only the field.
        m_input.onSubmit.AddListener(_ => Submit());
        UiBuilder.SetActive(m_input.gameObject, false);
        Place(false);
        m_panel.SetActive(false);
        m_log.Changed += OnLogChanged;
    }

    private static TMP_InputField CreateInput(UiBuilder ui, Transform parent)
    {
        GameObject fieldObject = UiBuilder.CreateUiObject("Input", parent);
        Image background = fieldObject.AddComponent<Image>();
        background.color = UiBuilder.ControlColor;
        GameObject area = UiBuilder.CreateUiObject("Text Area", fieldObject.transform);
        area.AddComponent<RectMask2D>();
        RectTransform viewport = UiBuilder.Stretch(area, 6f, 2f);
        TMP_Text text = ui.CreateLabel("Text", area.transform);
        UiBuilder.Stretch(text.gameObject, 0f, 0f);
        text.richText = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        field.targetGraphic = background;
        field.textViewport = viewport;
        field.textComponent = text;
        field.richText = false;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = ChatText.MaxLength;
        field.customCaretColor = true;
        field.caretColor = UiBuilder.TextColor;

        // Only what the chat text rule allows can be typed (Gameplay Systems §15).
        field.onValidateInput = (_, _, character) => character >= ' ' && character <= '~' ? character : '\0';
        return field;
    }

    // Lines from the top; the input takes the bottom row on the desktop and covers the last line with touch.
    private void Place(bool isTouch)
    {
        m_isTouchLayout = isTouch;
        Rect bounds = BoundsFor(isTouch);
        m_panelRect!.anchoredPosition = bounds.position;
        m_panelRect.sizeDelta = bounds.size;
        int rows = isTouch ? TouchLines : DesktopLines;
        float lineArea = bounds.height - 2f * Padding - (isTouch ? 0f : InputHeight);
        float lineHeight = lineArea / rows;
        for (int index = 0; index < m_labels.Count; index++)
        {
            TMP_Text label = m_labels[index];
            label.fontSize = isTouch ? TouchFontSize : DesktopFontSize;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-2f * Padding, lineHeight);
            rect.anchoredPosition = new Vector2(0f, -Padding - index * lineHeight);
            UiBuilder.SetActive(label.gameObject, index < rows);
        }

        var input = (RectTransform)m_input!.transform;
        input.anchorMin = new Vector2(0f, 0f);
        input.anchorMax = new Vector2(1f, 0f);
        input.pivot = new Vector2(0.5f, 0f);
        input.sizeDelta = new Vector2(-2f * Padding, InputHeight);
        input.anchoredPosition = new Vector2(0f, Padding);
        m_isLogChanged = true;
    }

    // The newest lines that fit, oldest first, and the prompt, while there is one, as the last.
    private void Rewrite()
    {
        int rows = m_isTouchLayout == true ? TouchLines : DesktopLines;
        IReadOnlyList<ChatLine> lines = m_log != null ? m_log.Lines : Array.Empty<ChatLine>();
        int fromLog = Math.Min(lines.Count, m_prompt != null ? rows - 1 : rows);
        int first = lines.Count - fromLog;
        m_text.Clear();
        int row = 0;
        for (int index = first; index < lines.Count; index++, row++)
        {
            Show(row, lines[index].Text, ColorOf(lines[index].Kind));
        }

        if (m_prompt != null)
        {
            Show(row++, m_prompt, SystemColor);
        }

        for (; row < m_labels.Count; row++)
        {
            UiBuilder.SetText(m_labels[row], string.Empty);
        }

        Text = m_text.ToString();
        TextChanges++;
        UiBuilder.SetActive(m_panel!, m_text.Length > 0 || IsTyping);
    }

    private void Show(int row, string text, Color color)
    {
        TMP_Text label = m_labels[row];
        UiBuilder.SetText(label, text);
        label.color = color;
        if (m_text.Length > 0)
        {
            m_text.Append('\n');
        }

        m_text.Append(text);
    }

    private static Color ColorOf(ChatLineKind kind)
    {
        switch (kind)
        {
            case ChatLineKind.System:
                return SystemColor;
            case ChatLineKind.Party:
                return PartyColor;
            case ChatLineKind.WhisperReceived:
            case ChatLineKind.WhisperSent:
                return WhisperColor;
            default:
                return UiBuilder.TextColor;
        }
    }

    private string? CurrentPrompt()
    {
        SkillDefinitionId skill = m_client != null ? m_client.TargetingSkill : default;
        if (skill == default)
        {
            return null;
        }

        ClientContent? content = m_client!.Content;
        SkillTargetType targetType = content != null
            && content.TryGetSkill(skill, out ClientSkill? definition)
            && definition != null
                ? definition.TargetType
                : SkillTargetType.Enemy;
        bool isTouch = m_client.Touch != null && m_client.Touch.IsVisible;
        return PromptFor(BuildMessages.SkillName(content, skill), targetType, isTouch);
    }
}
}
