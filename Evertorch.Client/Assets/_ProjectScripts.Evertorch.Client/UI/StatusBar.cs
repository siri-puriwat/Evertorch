using System;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The line along the top of the screen while in the world (Prototype Content §2): the character's name and level,
///     its HP, the map, and the round-trip time. Each part is rewritten only when its value changes.
/// </summary>
public sealed class StatusBar : MonoBehaviour
{
    // Under the login panel and the development overlay, over the combat HUD and the stick.
    private const int SortingOrder = 6;
    private const float Height = 56f;
    private const int Padding = 12;

    private static readonly UiBuilder Ui = new(24f, Height, 0f, 24f);

    private GameClient? m_client;
    private GameObject? m_bar;
    private TMP_Text? m_name;
    private TMP_Text? m_health;
    private TMP_Text? m_map;
    private TMP_Text? m_ping;
    private string? m_shownName;
    private int m_shownLevel = -1;
    private long m_shownHealth = -1;
    private long m_shownMaximum = -1;
    private bool m_shownDead;
    private string? m_shownMapName;
    private MapDefinitionId m_shownMap;
    private int m_shownPing = -1;

    /// <summary>
    ///     How many times a label was rewritten, which only a changed value may cause.
    /// </summary>
    public int TextChanges { get; private set; }

    public bool IsVisible => m_bar != null && m_bar.activeSelf;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        UiBuilder.SetActive(m_bar!, world != null);
        if (m_client == null || world == null)
        {
            return;
        }

        CharacterListEntry? played = m_client.PlayedCharacter;
        if (played.HasValue)
        {
            ShowCharacter(played.Value.Name, played.Value.BaseLevel);
        }

        ShowHealth(world.LocalHealth, world.LocalMaximumHealth, world.IsLocalDead);
        if (world.Map != m_shownMap)
        {
            m_shownMap = world.Map;
            ShowMap(MapName(m_client.Content, world.Map));
        }

        ShowPing(m_client.Connection?.RoundTripMilliseconds ?? 0);
    }

    public static StatusBar Create(GameClient client)
    {
        var root = new GameObject("StatusBar", typeof(RectTransform));
        root.SetActive(false);
        StatusBar bar = root.AddComponent<StatusBar>();
        bar.Build(client);
        root.SetActive(true);
        return bar;
    }

    public void ShowCharacter(string name, int level)
    {
        if (level == m_shownLevel && string.Equals(name, m_shownName, StringComparison.Ordinal))
        {
            return;
        }

        m_shownName = name;
        m_shownLevel = level;
        Write(m_name!, $"{name}   Lv {level}");
    }

    public void ShowHealth(uint current, uint maximum, bool isDead)
    {
        if (current == m_shownHealth && maximum == m_shownMaximum && isDead == m_shownDead)
        {
            return;
        }

        m_shownHealth = current;
        m_shownMaximum = maximum;
        m_shownDead = isDead;
        Write(m_health!, isDead ? $"HP 0 / {maximum}   You died" : $"HP {current} / {maximum}");
    }

    public void ShowMap(string displayName)
    {
        if (string.Equals(displayName, m_shownMapName, StringComparison.Ordinal))
        {
            return;
        }

        m_shownMapName = displayName;
        Write(m_map!, displayName);
    }

    public void ShowPing(int milliseconds)
    {
        if (milliseconds == m_shownPing)
        {
            return;
        }

        m_shownPing = milliseconds;
        Write(m_ping!, $"{milliseconds} ms");
    }

    private static string MapName(ClientContent? content, MapDefinitionId map)
    {
        return content != null && content.TryGetMap(map, out ClientMap? found) && found != null
            ? found.DisplayName
            : map.Value;
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        m_bar = UiBuilder.CreateUiObject("Bar", transform);
        var rect = (RectTransform)m_bar.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, Height);
        m_bar.AddComponent<Image>().color = UiBuilder.PanelColor;
        HorizontalLayoutGroup layout = m_bar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(Padding, Padding, 0, 0);
        layout.spacing = Padding;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        m_name = CreatePart("Name", TextAlignmentOptions.MidlineLeft);
        m_health = CreatePart("Health", TextAlignmentOptions.Midline);
        m_map = CreatePart("Map", TextAlignmentOptions.Midline);
        m_ping = CreatePart("Ping", TextAlignmentOptions.MidlineRight);
        m_bar.SetActive(false);
    }

    private TMP_Text CreatePart(string objectName, TextAlignmentOptions alignment)
    {
        TMP_Text part = Ui.CreateLabel(objectName, m_bar!.transform);
        part.alignment = alignment;
        part.textWrappingMode = TextWrappingModes.NoWrap;
        return part;
    }

    private void Write(TMP_Text label, string text)
    {
        label.text = text;
        TextChanges++;
    }
}
}
