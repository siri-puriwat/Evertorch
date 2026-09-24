using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The on-screen stick, the Next, Previous, and Clear target buttons, and the development overlay's toggle. The
///     stick and the target buttons drive a virtual gamepad, so the actions read them through the same bindings as a
///     real gamepad and no touch-specific movement or targeting code exists (Prototype Content §4).
/// </summary>
public sealed class TouchControls : MonoBehaviour
{
    public const string StickControlPath = "<Gamepad>/leftStick";
    public const string NextControlPath = "<Gamepad>/rightShoulder";
    public const string PreviousControlPath = "<Gamepad>/leftShoulder";
    public const string ClearControlPath = "<Gamepad>/buttonEast";

    private const float StickRange = 51f;
    private const float StickSize = 146f;
    private const float KnobSize = 62f;
    private const float StickInset = 129f;
    private const float ButtonGap = 16f;
    private const float ButtonInset = 32f;

    private static readonly Vector2 ButtonSize = new(190f, 96f);
    private static readonly Vector2 ToggleSize = new(110f, 64f);
    private static readonly Color AreaColor = new(1f, 1f, 1f, 0.18f);
    private static readonly Color KnobColor = new(1f, 1f, 1f, 0.55f);
    private static readonly UiBuilder Ui = new(30f, 0f, 0f, 0f);

    private GameObject? m_stickRoot;
    private GameObject? m_targetButtons;
    private GameObject? m_overlayToggle;

    public bool IsVisible => m_stickRoot != null && m_stickRoot.activeSelf;

    public RectTransform? StickArea { get; private set; }

    public RectTransform? Knob { get; private set; }

    /// <param name="toggleOverlay">What the overlay's toggle does; without it there is no toggle.</param>
    public static TouchControls Create(UnityAction? toggleOverlay = null)
    {
        var root = new GameObject("TouchControls");
        TouchControls controls = root.AddComponent<TouchControls>();
        controls.Build(toggleOverlay);
        return controls;
    }

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

        if (m_overlayToggle != null)
        {
            m_overlayToggle.SetActive(isVisible);
        }
    }

    private void Build(UnityAction? toggleOverlay)
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

        if (toggleOverlay != null)
        {
            m_overlayToggle = CreateOverlayToggle(canvasObject.transform, toggleOverlay);
        }
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

    // On the left edge, clear of the stick: the overlay's F1 for a device without a keyboard.
    private static GameObject CreateOverlayToggle(Transform parent, UnityAction toggleOverlay)
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
        return toggle;
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
