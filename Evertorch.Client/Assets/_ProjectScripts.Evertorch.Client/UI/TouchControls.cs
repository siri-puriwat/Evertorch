using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The on-screen stick, the Next, Previous, and Clear target buttons, the Stats and Skills buttons, and the
///     development overlay's toggle. The stick and the other buttons but the toggle drive a virtual gamepad, so the
///     actions read them through the same bindings as a real gamepad and no touch-specific movement, targeting, or
///     window code exists (Prototype Content §4).
/// </summary>
public sealed class TouchControls : MonoBehaviour
{
    public const string StickControlPath = "<Gamepad>/leftStick";
    public const string NextControlPath = "<Gamepad>/rightShoulder";
    public const string PreviousControlPath = "<Gamepad>/leftShoulder";
    public const string ClearControlPath = "<Gamepad>/buttonEast";
    public const string StatsControlPath = "<Gamepad>/select";
    public const string SkillsControlPath = "<Gamepad>/leftStickPress";

    private const float StickRange = 51f;
    private const float StickSize = 146f;
    private const float KnobSize = 62f;
    private const float StickInset = 129f;
    private const float ButtonGap = 16f;
    private const float ButtonInset = 32f;
    private const int TargetButtonCount = 3;

    private static readonly Vector2 ButtonSize = new(190f, 96f);
    private static readonly Vector2 ToggleSize = new(110f, 64f);
    private static readonly Color AreaColor = new(1f, 1f, 1f, 0.18f);
    private static readonly Color KnobColor = new(1f, 1f, 1f, 0.55f);
    private static readonly UiBuilder Ui = new(30f, 0f, 0f, 0f);

    private GameObject? m_stickRoot;
    private GameObject? m_targetButtons;
    private GameObject? m_windowButtons;
    private GameObject? m_chatButton;

    public bool IsVisible => m_stickRoot != null && m_stickRoot.activeSelf;

    public RectTransform? StickArea { get; private set; }

    public RectTransform? Knob { get; private set; }

    /// <summary>
    ///     What the stick and its knob can cover, in canvas units from the bottom-left corner.
    /// </summary>
    public static Rect StickBounds
    {
        get
        {
            float reach = Mathf.Max(StickSize / 2f, StickRange + KnobSize / 2f);
            return new Rect(StickInset - reach, StickInset - reach, 2f * reach, 2f * reach);
        }
    }

    /// <summary>
    ///     The right edge of the Dev, Stats, and Skills buttons on the left edge, in canvas units.
    /// </summary>
    public static float WindowButtonsRight => ToggleSize.x;

    /// <summary>
    ///     The Chat button, between the chat log and the target buttons, from the canvas's bottom-left corner
    ///     (Prototype Content §2, §4).
    /// </summary>
    public static Rect ChatButtonBounds => new(770f, 128f, 80f, 72f);

    /// <summary>
    ///     The column of target buttons in the bottom right corner of a canvas <paramref name="canvasWidth" /> units
    ///     wide, from its bottom-left corner.
    /// </summary>
    public static Rect ButtonColumnBounds(float canvasWidth)
    {
        float height = TargetButtonCount * ButtonSize.y + (TargetButtonCount - 1) * ButtonGap;
        return new Rect(canvasWidth - ButtonInset - ButtonSize.x, ButtonInset, ButtonSize.x, height);
    }

    /// <summary>
    ///     The Stats button on the left edge of a canvas <paramref name="canvasHeight" /> units tall, just above the Dev
    ///     button's place, from its bottom-left corner.
    /// </summary>
    public static Rect StatsButtonBounds(float canvasHeight)
    {
        return WindowButtonBounds(canvasHeight, 0);
    }

    /// <summary>
    ///     The Skills button, just above the Stats button, from the canvas's bottom-left corner.
    /// </summary>
    public static Rect SkillsButtonBounds(float canvasHeight)
    {
        return WindowButtonBounds(canvasHeight, 1);
    }

    /// <summary>
    ///     The Dev button on the left edge of a canvas <paramref name="canvasHeight" /> units tall, from its bottom-left
    ///     corner.
    /// </summary>
    public static Rect OverlayToggleBounds(float canvasHeight)
    {
        return new Rect(0f, canvasHeight / 2f - ToggleSize.y / 2f, ToggleSize.x, ToggleSize.y);
    }

    /// <param name="toggleOverlay">What the Dev button does; without it there is no Dev button.</param>
    /// <param name="openChat">What the Chat button does; without it there is no Chat button.</param>
    public static TouchControls Create(UnityAction? toggleOverlay = null, UnityAction? openChat = null)
    {
        var root = new GameObject("TouchControls");
        TouchControls controls = root.AddComponent<TouchControls>();
        controls.Build(toggleOverlay, openChat);
        return controls;
    }

    /// <summary>
    ///     Shows or hides the stick, the target buttons, and the Stats and Skills buttons. The Dev button stays: on a device
    ///     without
    ///     a keyboard it is the only way back to the overlay, whose toggle calls this.
    /// </summary>
    public void SetVisible(bool isVisible)
    {
        if (m_stickRoot != null)
        {
            m_stickRoot.SetActive(isVisible);
        }

        if (m_targetButtons != null)
        {
            m_targetButtons.SetActive(isVisible);
        }

        if (m_windowButtons != null)
        {
            m_windowButtons.SetActive(isVisible);
        }

        if (m_chatButton != null)
        {
            m_chatButton.SetActive(isVisible);
        }
    }

    private void Build(UnityAction? toggleOverlay, UnityAction? openChat)
    {
        ClientUI.EnsureEventSystem(transform);

        var canvasObject = new GameObject("Canvas");
        canvasObject.transform.SetParent(transform, false);
        ClientUI.AddScreenCanvas(canvasObject, 0);

        m_stickRoot = CreateImage("Stick", canvasObject.transform, new Vector2(StickSize, StickSize), AreaColor);
        StickArea = (RectTransform)m_stickRoot.transform;
        StickArea.anchorMin = Vector2.zero;
        StickArea.anchorMax = Vector2.zero;
        StickArea.anchoredPosition = new Vector2(StickInset, StickInset);

        // An on-screen control reads its control path when it is enabled, so the path has to be in place before that.
        GameObject knob = CreateImage("Knob", m_stickRoot.transform, new Vector2(KnobSize, KnobSize), KnobColor);
        knob.SetActive(false);
        OnScreenStick stick = knob.AddComponent<OnScreenStick>();
        stick.controlPath = StickControlPath;
        stick.movementRange = StickRange;
        knob.SetActive(true);
        Knob = (RectTransform)knob.transform;

        m_targetButtons = UiBuilder.CreateUiObject("Target buttons", canvasObject.transform);
        UiBuilder.Stretch(m_targetButtons, 0f, 0f);
        CreateTargetButton("Clear", ClearControlPath, 0);
        CreateTargetButton("Previous", PreviousControlPath, 1);
        CreateTargetButton("Next", NextControlPath, 2);

        m_windowButtons = UiBuilder.CreateUiObject("Window buttons", canvasObject.transform);
        UiBuilder.Stretch(m_windowButtons, 0f, 0f);
        CreateWindowButton("Stats", StatsControlPath, 0);
        CreateWindowButton("Skills", SkillsControlPath, 1);

        if (toggleOverlay != null)
        {
            CreateOverlayToggle(canvasObject.transform, toggleOverlay);
        }

        if (openChat != null)
        {
            m_chatButton = CreateChatButton(canvasObject.transform, openChat);
        }
    }

    // A tap opens the chat input, and with it the device's keyboard; the gamepad only reads the log.
    private static GameObject CreateChatButton(Transform parent, UnityAction openChat)
    {
        Rect bounds = ChatButtonBounds;
        GameObject chat = CreateImage("Chat", parent, bounds.size, AreaColor);
        var rect = (RectTransform)chat.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = bounds.position;
        Button button = chat.AddComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(openChat);
        AddLabel("Chat", chat.transform);
        return chat;
    }

    // A column in the bottom right corner, Clear at the bottom, under the right thumb.
    private void CreateTargetButton(string label, string controlPath, int row)
    {
        GameObject button = CreateImage(label, m_targetButtons!.transform, ButtonSize, AreaColor);
        var rect = (RectTransform)button.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-ButtonInset, ButtonInset + row * (ButtonSize.y + ButtonGap));
        AddLabel(label, button.transform);

        button.SetActive(false);
        button.AddComponent<OnScreenButton>().controlPath = controlPath;
        button.SetActive(true);
    }

    private static Rect WindowButtonBounds(float canvasHeight, int row)
    {
        float bottom = canvasHeight / 2f + ToggleSize.y / 2f + ButtonGap + row * (ToggleSize.y + ButtonGap);
        return new Rect(0f, bottom, ToggleSize.x, ToggleSize.y);
    }

    // On the left edge above the Dev button's place, one above the other, clear of the stick.
    private void CreateWindowButton(string label, string controlPath, int row)
    {
        GameObject button = CreateImage(label, m_windowButtons!.transform, ToggleSize, AreaColor);
        var rect = (RectTransform)button.transform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(0f, ToggleSize.y / 2f + ButtonGap + row * (ToggleSize.y + ButtonGap));
        AddLabel(label, button.transform);

        button.SetActive(false);
        button.AddComponent<OnScreenButton>().controlPath = controlPath;
        button.SetActive(true);
    }

    // On the left edge, clear of the stick: the overlay's F1 for a device without a keyboard.
    private static void CreateOverlayToggle(Transform parent, UnityAction toggleOverlay)
    {
        GameObject toggle = CreateImage("Dev", parent, ToggleSize, AreaColor);
        var rect = (RectTransform)toggle.transform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        Button button = toggle.AddComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(toggleOverlay);
        AddLabel("Dev", toggle.transform);
    }

    private static void AddLabel(string text, Transform parent)
    {
        TMP_Text label = Ui.CreateLabel("Label", parent);
        UiBuilder.Stretch(label.gameObject, 0f, 0f);
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
    }

    private static GameObject CreateImage(string objectName, Transform parent, Vector2 size, Color color)
    {
        GameObject imageObject = UiBuilder.CreateUiObject(objectName, parent);
        imageObject.AddComponent<Image>().color = color;
        ((RectTransform)imageObject.transform).sizeDelta = size;
        return imageObject;
    }
}
}
