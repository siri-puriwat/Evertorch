using TMPro;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A hit, miss, or critical rising over its target and fading out. It removes itself when done.
/// </summary>
public sealed class FloatingNumber : MonoBehaviour
{
    private const float RiseMeters = 0.8f;
    private const float FontSize = 4f;
    private static readonly Vector2 Area = new(4f, 1f);

    private TextMeshPro? m_text;
    private Color m_color;
    private Vector3 m_origin;
    private float m_age;

    public string Text => m_text != null ? m_text.text : string.Empty;

    private void LateUpdate()
    {
        m_age += Time.deltaTime;
        float progress = m_age / (float)CombatAnimation.NumberSeconds;
        if (progress >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = m_origin + Vector3.up * (RiseMeters * progress);
        Camera? facing = Camera.main;
        if (facing != null)
        {
            transform.rotation = facing.transform.rotation;
        }

        if (m_text != null)
        {
            m_text.color = new Color(m_color.r, m_color.g, m_color.b, 1f - progress * progress);
        }
    }

    public static FloatingNumber Create(string text, Color color, float scale, Vector3 position)
    {
        var root = new GameObject("FloatingNumber");
        root.transform.position = position;
        FloatingNumber number = root.AddComponent<FloatingNumber>();
        number.m_origin = position;
        number.m_color = color;
        number.m_text = root.AddComponent<TextMeshPro>();
        number.m_text.text = text;
        number.m_text.fontSize = FontSize * scale;
        number.m_text.alignment = TextAlignmentOptions.Center;
        number.m_text.color = color;
        number.m_text.rectTransform.sizeDelta = Area;
        return number;
    }
}
}
