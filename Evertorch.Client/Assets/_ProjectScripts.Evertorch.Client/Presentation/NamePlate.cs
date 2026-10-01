using System;
using TMPro;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A name under an entity's feet, turned to the camera (Prototype Content §2). Rich text is off, so a name is
///     always drawn as the letters it holds.
/// </summary>
public sealed class NamePlate : MonoBehaviour
{
    private const float FontSize = 2.4f;

    // Below the feet on screen: along the camera's down direction, so the label sits under the body at any pitch.
    private const float DropMeters = 0.3f;

    private static readonly Vector2 Area = new(4f, 0.6f);
    private static readonly Color TextColor = new(1f, 1f, 1f);

    private TextMeshPro? m_text;

    public string Text => m_text != null ? m_text.text : string.Empty;

    public bool IsRichText => m_text != null && m_text.richText;

    public static NamePlate Create(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var root = new GameObject("NamePlate");
        NamePlate plate = root.AddComponent<NamePlate>();
        plate.m_text = root.AddComponent<TextMeshPro>();
        plate.m_text.richText = false;
        plate.m_text.text = text;
        plate.m_text.fontSize = FontSize;
        plate.m_text.alignment = TextAlignmentOptions.Center;
        plate.m_text.color = TextColor;
        plate.m_text.rectTransform.sizeDelta = Area;
        return plate;
    }

    public void SetText(string text)
    {
        if (m_text != null && !string.Equals(m_text.text, text, StringComparison.Ordinal))
        {
            m_text.text = text;
        }
    }

    public void Show(Vector3 feet, Camera? facing)
    {
        if (facing != null)
        {
            Transform view = facing.transform;
            transform.SetPositionAndRotation(feet - view.up * DropMeters, view.rotation);
        }
        else
        {
            transform.position = feet + Vector3.down * DropMeters;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }
}
}
