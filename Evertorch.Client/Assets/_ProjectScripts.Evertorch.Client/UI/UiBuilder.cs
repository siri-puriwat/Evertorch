using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The controls every client panel is built from, in one size per use: small for the development overlay, larger
///     for the panels a player sees.
/// </summary>
/// <remarks>
///     A clicked control is never selected, input fields included: the UI navigate action shares WASD, the arrow keys,
///     and the gamepad stick with movement, so a selected control would change while the player walks.
/// </remarks>
public sealed class UiBuilder
{
    private const float HandleWidth = 10f;
    private const int FieldCharacterLimit = 64;
    public static readonly Color PanelColor = new(0.08f, 0.09f, 0.11f, 0.85f);
    public static readonly Color ControlColor = new(0.24f, 0.27f, 0.32f, 1f);
    public static readonly Color AccentColor = new(0.25f, 0.65f, 0.95f);
    public static readonly Color TextColor = new(0.92f, 0.94f, 0.96f);

    private static readonly Navigation NoNavigation = new() { mode = Navigation.Mode.None };

    private readonly float m_fontSize;
    private readonly float m_rowHeight;
    private readonly float m_labelWidth;
    private readonly float m_spacing;

    public UiBuilder(float fontSize, float rowHeight, float labelWidth, float spacing)
    {
        m_fontSize = fontSize;
        m_rowHeight = rowHeight;
        m_labelWidth = labelWidth;
        m_spacing = spacing;
    }

    public static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    public static RectTransform Stretch(GameObject target, float insetX, float insetY)
    {
        var rect = (RectTransform)target.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(insetX, insetY);
        rect.offsetMax = new Vector2(-insetX, -insetY);
        return rect;
    }

    public static Image CreateImage(string objectName, Transform parent, Color color, float insetX, float insetY)
    {
        GameObject imageObject = CreateUiObject(objectName, parent);
        Stretch(imageObject, insetX, insetY);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    /// <summary>
    ///     A panel that grows with its column of controls, pinned by <paramref name="anchor" /> to an edge or a corner
    ///     of the screen.
    /// </summary>
    public RectTransform CreatePanel(Transform parent, Vector2 anchor, Vector2 offset, float width, int padding)
    {
        GameObject panel = CreateUiObject("Panel", parent);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(width, 0f);
        panel.AddComponent<Image>().color = PanelColor;
        AddColumnLayout(panel, padding);
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rect;
    }

    public void AddColumnLayout(GameObject target, int padding)
    {
        VerticalLayoutGroup layout = target.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = m_spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    public GameObject CreateColumn(string objectName, Transform parent)
    {
        GameObject column = CreateUiObject(objectName, parent);
        AddColumnLayout(column, 0);
        return column;
    }

    public GameObject CreateRow(string objectName, Transform parent)
    {
        GameObject row = CreateUiObject(objectName, parent);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = m_spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        row.AddComponent<LayoutElement>().preferredHeight = m_rowHeight;
        return row;
    }

    public TMP_Text CreateLabel(string objectName, Transform parent)
    {
        TextMeshProUGUI label = CreateUiObject(objectName, parent).AddComponent<TextMeshProUGUI>();

        // A label added in code reads null until its text is first set.
        label.text = string.Empty;
        label.fontSize = m_fontSize;
        label.color = TextColor;
        label.raycastTarget = false;
        return label;
    }

    public TMP_Text CreateRowLabel(string text, Transform row)
    {
        TMP_Text label = CreateLabel("Label", row);
        label.text = text;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = m_labelWidth;
        return label;
    }

    /// <summary>
    ///     A button whose object is named after its first label, which is how tests find it.
    /// </summary>
    public GameObject CreateButton(string label, Transform parent, UnityAction onClick)
    {
        GameObject buttonObject = CreateUiObject(label, parent);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = m_rowHeight;
        Image background = buttonObject.AddComponent<Image>();
        background.color = ControlColor;
        Button button = buttonObject.AddComponent<Button>();
        button.navigation = NoNavigation;
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        TMP_Text text = CreateLabel("Label", buttonObject.transform);
        Stretch(text.gameObject, 0f, 0f);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        return buttonObject;
    }

    /// <summary>
    ///     A labelled input field in a row named after the label.
    /// </summary>
    public TMP_InputField CreateField(
        Transform parent,
        string label,
        string value,
        TMP_InputField.ContentType contentType)
    {
        GameObject row = CreateRow(label, parent);
        CreateRowLabel(label, row.transform);

        GameObject fieldObject = CreateUiObject("Field", row.transform);
        fieldObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Image background = fieldObject.AddComponent<Image>();
        background.color = ControlColor;
        GameObject area = CreateUiObject("Text Area", fieldObject.transform);
        area.AddComponent<RectMask2D>();
        RectTransform viewport = Stretch(area, 6f, 2f);
        TMP_Text text = CreateLabel("Text", area.transform);
        Stretch(text.gameObject, 0f, 0f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
        field.navigation = NoNavigation;
        field.targetGraphic = background;
        field.textViewport = viewport;
        field.textComponent = text;
        field.customCaretColor = true;
        field.caretColor = TextColor;
        field.contentType = contentType;
        field.characterLimit = FieldCharacterLimit;
        field.text = value;
        return field;
    }

    public Slider CreateSlider(Transform parent, int max)
    {
        GameObject sliderObject = CreateUiObject("Slider", parent);
        sliderObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateImage("Background", sliderObject.transform, ControlColor, 0f, 8f);
        GameObject fillArea = CreateUiObject("Fill Area", sliderObject.transform);
        Stretch(fillArea, HandleWidth / 2f, 8f);
        Image fill = CreateImage("Fill", fillArea.transform, AccentColor, 0f, 0f);
        GameObject handleArea = CreateUiObject("Handle Area", sliderObject.transform);
        Stretch(handleArea, HandleWidth / 2f, 0f);
        Image handle = CreateImage("Handle", handleArea.transform, TextColor, 0f, 0f);
        handle.rectTransform.sizeDelta = new Vector2(HandleWidth, 0f);

        Slider slider = sliderObject.AddComponent<Slider>();
        slider.navigation = NoNavigation;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.wholeNumbers = true;
        slider.maxValue = max;
        return slider;
    }

    public Toggle CreateToggle(Transform parent, string label, UnityAction<bool> onChanged)
    {
        GameObject row = CreateRow(label, parent);
        GameObject box = CreateUiObject("Box", row.transform);
        box.AddComponent<LayoutElement>().preferredWidth = m_rowHeight;
        Image background = box.AddComponent<Image>();
        background.color = ControlColor;
        Image check = CreateImage("Check", box.transform, AccentColor, 6f, 6f);
        TMP_Text text = CreateLabel("Label", row.transform);
        text.text = label;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = true;

        Toggle toggle = row.AddComponent<Toggle>();
        toggle.navigation = NoNavigation;
        toggle.targetGraphic = background;
        toggle.graphic = check;
        toggle.onValueChanged.AddListener(onChanged);
        return toggle;
    }

    /// <summary>
    ///     Replaces the label's text only when it differs, so an unchanged panel costs no mesh rebuild.
    /// </summary>
    public static void SetText(TMP_Text label, string text)
    {
        if (!string.Equals(label.text, text, StringComparison.Ordinal))
        {
            label.text = text;
        }
    }

    public static void SetActive(GameObject target, bool isActive)
    {
        if (target.activeSelf != isActive)
        {
            target.SetActive(isActive);
        }
    }
}
}
