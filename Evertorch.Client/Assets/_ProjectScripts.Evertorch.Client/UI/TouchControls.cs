using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
/// The on-screen stick. It drives a virtual gamepad stick, so the movement action reads it through the same
/// binding as a real gamepad and no touch-specific movement code exists.
/// </summary>
public sealed class TouchControls : MonoBehaviour
{
    public const string StickControlPath = "<Gamepad>/leftStick";

    private const float StickRange = 90f;
    private const float StickSize = 260f;
    private const float KnobSize = 110f;

    private readonly List<RaycastResult> m_raycastResults = new List<RaycastResult>();
    private GameObject? m_stickRoot;
    private EventSystem? m_eventSystem;

    public bool IsVisible => m_stickRoot != null && m_stickRoot.activeSelf;

    public RectTransform? StickArea { get; private set; }

    public RectTransform? Knob { get; private set; }

    public static TouchControls Create()
    {
        GameObject root = new GameObject("TouchControls");
        TouchControls controls = root.AddComponent<TouchControls>();
        controls.Build();
        return controls;
    }

    public void SetVisible(bool isVisible)
    {
        if (m_stickRoot != null)
        {
            m_stickRoot.SetActive(isVisible);
        }
    }

    /// <summary>
    /// Whether a screen position is on a control, so that touching the stick is never also a tap on the ground.
    /// </summary>
    public bool IsOverControl(Vector2 screenPosition)
    {
        if (m_eventSystem == null)
        {
            return false;
        }

        PointerEventData pointer = new PointerEventData(m_eventSystem) { position = screenPosition };
        m_raycastResults.Clear();
        m_eventSystem.RaycastAll(pointer, m_raycastResults);
        return m_raycastResults.Count > 0;
    }

    private void Build()
    {
        m_eventSystem = EventSystem.current;
        if (m_eventSystem == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.transform.SetParent(transform, false);
            m_eventSystem = eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        GameObject canvasObject = new GameObject("Canvas");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObject.AddComponent<GraphicRaycaster>();

        m_stickRoot = CreateImage("Stick", canvasObject.transform, StickSize, new Color(1f, 1f, 1f, 0.18f));
        StickArea = (RectTransform)m_stickRoot.transform;
        StickArea.anchorMin = Vector2.zero;
        StickArea.anchorMax = Vector2.zero;
        StickArea.pivot = new Vector2(0.5f, 0.5f);
        StickArea.anchoredPosition = new Vector2(230f, 230f);

        // The stick reads its control path when it is enabled, so the path has to be in place before that.
        GameObject knob = CreateImage("Knob", m_stickRoot.transform, KnobSize, new Color(1f, 1f, 1f, 0.55f));
        knob.SetActive(false);
        OnScreenStick stick = knob.AddComponent<OnScreenStick>();
        stick.controlPath = StickControlPath;
        stick.movementRange = StickRange;
        knob.SetActive(true);
        Knob = (RectTransform)knob.transform;
    }

    private static GameObject CreateImage(string objectName, Transform parent, float size, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        ((RectTransform)imageObject.transform).sizeDelta = new Vector2(size, size);
        return imageObject;
    }
}
}
