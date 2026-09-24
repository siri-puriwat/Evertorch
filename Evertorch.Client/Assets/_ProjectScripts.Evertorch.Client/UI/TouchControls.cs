using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The on-screen stick. It drives a virtual gamepad stick, so the movement action reads it through the same
///     binding as a real gamepad and no touch-specific movement code exists.
/// </summary>
public sealed class TouchControls : MonoBehaviour
{
    public const string StickControlPath = "<Gamepad>/leftStick";

    private const float StickRange = 51f;
    private const float StickSize = 146f;
    private const float KnobSize = 62f;
    private const float StickInset = 129f;

    private GameObject? m_stickRoot;

    public bool IsVisible => m_stickRoot != null && m_stickRoot.activeSelf;

    public RectTransform? StickArea { get; private set; }

    public RectTransform? Knob { get; private set; }

    public static TouchControls Create()
    {
        var root = new GameObject("TouchControls");
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

    private void Build()
    {
        ClientUI.EnsureEventSystem(transform);

        var canvasObject = new GameObject("Canvas");
        canvasObject.transform.SetParent(transform, false);
        ClientUI.AddScreenCanvas(canvasObject, 0);

        m_stickRoot = CreateImage("Stick", canvasObject.transform, StickSize, new Color(1f, 1f, 1f, 0.18f));
        StickArea = (RectTransform)m_stickRoot.transform;
        StickArea.anchorMin = Vector2.zero;
        StickArea.anchorMax = Vector2.zero;
        StickArea.pivot = new Vector2(0.5f, 0.5f);
        StickArea.anchoredPosition = new Vector2(StickInset, StickInset);

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
        var imageObject = new GameObject(objectName, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        ((RectTransform)imageObject.transform).sizeDelta = new Vector2(size, size);
        return imageObject;
    }
}
}
