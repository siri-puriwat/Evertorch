using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The line along the top of the screen while in the world (Prototype Content §2): the character's name and level,
///     its HP and SP, its status effects with their seconds left, its active quests with their progress, the map, and
///     the round-trip time, over a thin experience bar. Each part is rewritten only when its value changes. The name comes
///     from the character list, the level from the world (owner decision 9).
/// </summary>
public sealed class StatusBar : MonoBehaviour
{
    // Under the login panel and the development overlay, over the combat HUD and the stick.
    private const int SortingOrder = 6;
    private const float Height = 56f;
    private const int Padding = 12;
    private const float ExperienceBarHeight = 6f;
    private const float JobExperienceBarHeight = 4f;

    private static readonly UiBuilder Ui = new(24f, Height, 0f, 24f);
    private static readonly Color ExperienceBackColor = new(0.15f, 0.16f, 0.2f);
    private static readonly Color ExperienceFillColor = new(0.95f, 0.78f, 0.25f);
    private static readonly Color JobExperienceFillColor = new(0.35f, 0.72f, 0.95f);
    private readonly List<(StatusDefinitionId Status, int Seconds)> m_effectsNow = new();
    private readonly List<(StatusDefinitionId Status, int Seconds)> m_shownEffects = new();

    private GameClient? m_client;
    private GameObject? m_bar;
    private TMP_Text? m_name;
    private TMP_Text? m_health;
    private TMP_Text? m_spirit;
    private TMP_Text? m_effects;
    private TMP_Text? m_quests;
    private RectTransform? m_experienceFill;
    private RectTransform? m_jobExperienceFill;
    private int m_shownJobExperiencePermille = -1;
    private string? m_shownJob;
    private int m_shownJobLevel = -1;
    private TMP_Text? m_map;
    private TMP_Text? m_ping;
    private string? m_shownName;
    private int m_shownLevel = -1;
    private long m_shownHealth = -1;
    private long m_shownMaximum = -1;
    private bool m_shownDead;
    private long m_shownSpirit = -1;
    private long m_shownSpiritMaximum = -1;
    private int m_shownExperiencePermille = -1;
    private string? m_shownMapName;
    private MapDefinitionId m_shownMap;
    private int m_shownPing = -1;
    private IReadOnlyList<QuestLogEntry>? m_shownQuests;
    private int m_shownOffersSeen = -1;
    private ClientContent? m_shownQuestContent;

    /// <summary>
    ///     How many times a label was rewritten, which only a changed value may cause.
    /// </summary>
    public int TextChanges { get; private set; }

    public bool IsVisible => m_bar != null && m_bar.activeSelf;

    /// <summary>
    ///     The filled part of the experience bar, from 0 to 1.
    /// </summary>
    public float ShownExperienceRatio => m_experienceFill != null ? m_experienceFill.anchorMax.x : 0f;

    /// <summary>
    ///     The filled part of the job-experience bar, from 0 to 1.
    /// </summary>
    public float ShownJobExperienceRatio => m_jobExperienceFill != null ? m_jobExperienceFill.anchorMax.x : 0f;

    /// <summary>
    ///     The active quests as shown, "Forest Crawler 3/5" each; empty while none is active.
    /// </summary>
    public string QuestText => m_quests != null ? m_quests.text : string.Empty;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        UiBuilder.SetActive(m_bar!, world != null);
        if (m_client == null || world == null)
        {
            return;
        }

        CharacterListEntry? played = m_client.PlayedCharacter;
        CharacterSheet? sheet = world.Sheet;
        if (played.HasValue)
        {
            ShowCharacter(played.Value.Name, world.Level, JobName(m_client.Content, world.LocalJob),
                sheet?.JobLevel ?? 0);
        }

        ShowHealth(world.LocalHealth, world.LocalMaximumHealth, world.IsLocalDead);
        ShowSpirit(world.LocalSpirit, world.LocalMaximumSpirit);
        ShowEffects(world, m_client.Content);
        ShowQuests(world.Quests, m_client.Connection, m_client.Content);
        ShowExperience(world.Experience, world.ExperienceToNextLevel);
        if (sheet != null)
        {
            ShowJobExperience(sheet.JobExperience, sheet.JobExperienceToNextLevel);
        }

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

    /// <summary>
    ///     "Ann   Lv 3   Adventurer Lv 2": the character, its base level, and its job with the job level, which comes with
    ///     the character sheet; job level 0 leaves the job out until then (Prototype Content §2).
    /// </summary>
    public void ShowCharacter(string name, int level, string job, int jobLevel)
    {
        if (level == m_shownLevel
            && jobLevel == m_shownJobLevel
            && string.Equals(name, m_shownName, StringComparison.Ordinal)
            && string.Equals(job, m_shownJob, StringComparison.Ordinal))
        {
            return;
        }

        m_shownName = name;
        m_shownLevel = level;
        m_shownJob = job;
        m_shownJobLevel = jobLevel;
        Write(m_name!, jobLevel > 0 ? $"{name}   Lv {level}   {job} Lv {jobLevel}" : $"{name}   Lv {level}");
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

    public void ShowSpirit(uint current, uint maximum)
    {
        if (current == m_shownSpirit && maximum == m_shownSpiritMaximum)
        {
            return;
        }

        m_shownSpirit = current;
        m_shownSpiritMaximum = maximum;
        Write(m_spirit!, $"SP {current} / {maximum}");
    }

    // Whole seconds, rounded up; an effect whose time ran out here is left out while its end is on its way. The text
    // is built only when a name or a second changes.
    private void ShowEffects(ClientWorld world, ClientContent? content)
    {
        m_effectsNow.Clear();
        foreach (StatusEffectEntry effect in world.StatusEffects)
        {
            int seconds = (int)Math.Ceiling(world.StatusRemaining(effect.Status));
            if (seconds > 0)
            {
                m_effectsNow.Add((effect.Status, seconds));
            }
        }

        if (IsShown(m_effectsNow))
        {
            return;
        }

        m_shownEffects.Clear();
        m_shownEffects.AddRange(m_effectsNow);
        var text = new StringBuilder();
        foreach ((StatusDefinitionId status, int seconds) in m_shownEffects)
        {
            if (text.Length > 0)
            {
                text.Append("   ");
            }

            string name = content != null && content.TryGetStatusEffect(status, out ClientStatusEffect? found)
                && found != null
                    ? found.DisplayName
                    : status.Value;
            text.Append(name).Append(' ').Append(seconds.ToString(CultureInfo.InvariantCulture)).Append('s');
        }

        Write(m_effects!, text.ToString());
    }

    // Each active quest by its objective, "Forest Crawler 3/5" and "(ready)" at its count (Prototype Content §2): the
    // monster comes from the offer this session saw, the quest's own name standing in until one comes. Rewritten only
    // for a new quest log, a newly seen offer, or new names.
    private void ShowQuests(IReadOnlyList<QuestLogEntry> quests, ClientConnection? connection, ClientContent? content)
    {
        int offersSeen = connection?.QuestOffersSeen ?? 0;
        if (quests == m_shownQuests && offersSeen == m_shownOffersSeen && content == m_shownQuestContent)
        {
            return;
        }

        m_shownQuests = quests;
        m_shownOffersSeen = offersSeen;
        m_shownQuestContent = content;
        var text = new StringBuilder();
        foreach (QuestLogEntry entry in quests)
        {
            if (entry.State != QuestState.Active)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append("   ");
            }

            NpcQuestOffer? offer =
                connection != null && connection.TryGetQuestOffer(entry.Quest, out NpcQuestOffer seen)
                    ? seen
                    : null;
            text.Append(QuestMessages.Progress(entry, offer, content));
        }

        string shown = text.ToString();
        if (!string.Equals(shown, m_quests!.text, StringComparison.Ordinal))
        {
            Write(m_quests!, shown);
        }
    }

    private bool IsShown(List<(StatusDefinitionId Status, int Seconds)> effects)
    {
        if (effects.Count != m_shownEffects.Count)
        {
            return false;
        }

        for (int index = 0; index < effects.Count; index++)
        {
            if (!effects[index].Equals(m_shownEffects[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Fills the experience bar by the share of the next level earned; full at the level cap, where
    ///     <paramref name="toNextLevel" /> is 0.
    /// </summary>
    public void ShowExperience(ulong experience, ulong toNextLevel)
    {
        int permille = toNextLevel == 0
            ? 1000
            : (int)Math.Min(1000.0, Math.Floor(experience * 1000.0 / toNextLevel));
        if (permille == m_shownExperiencePermille)
        {
            return;
        }

        m_shownExperiencePermille = permille;
        m_experienceFill!.anchorMax = new Vector2(permille / 1000f, 1f);
    }

    /// <summary>
    ///     Fills the job-experience bar under the experience bar by the share of the next job level earned; full at the
    ///     job's cap.
    /// </summary>
    public void ShowJobExperience(ulong experience, ulong toNextLevel)
    {
        int permille = toNextLevel == 0
            ? 1000
            : (int)Math.Min(1000.0, Math.Floor(experience * 1000.0 / toNextLevel));
        if (permille == m_shownJobExperiencePermille)
        {
            return;
        }

        m_shownJobExperiencePermille = permille;
        m_jobExperienceFill!.anchorMax = new Vector2(permille / 1000f, 1f);
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

    private static string JobName(ClientContent? content, JobDefinitionId job)
    {
        return content != null && content.TryGetJob(job, out ClientJob? found) && found != null
            ? found.DisplayName
            : job.Value;
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
        m_spirit = CreatePart("Spirit", TextAlignmentOptions.Midline);
        m_effects = CreatePart("Effects", TextAlignmentOptions.Midline);
        m_quests = CreatePart("Quests", TextAlignmentOptions.Midline);

        // A quest's objective and progress take more room than the other parts, and shrink to fit on a narrow screen.
        m_quests.gameObject.AddComponent<LayoutElement>().flexibleWidth = 2f;
        m_quests.enableAutoSizing = true;
        m_quests.fontSizeMin = 12f;
        m_quests.fontSizeMax = m_quests.fontSize;
        m_map = CreatePart("Map", TextAlignmentOptions.Midline);
        m_ping = CreatePart("Ping", TextAlignmentOptions.MidlineRight);

        // Along the bar's bottom edge, outside its row of labels: the job's strip at the very bottom and the base
        // experience's above it.
        m_jobExperienceFill = CreateStrip("JobExperience", JobExperienceFillColor, 0f, JobExperienceBarHeight);
        m_experienceFill = CreateStrip(
            "Experience",
            ExperienceFillColor,
            JobExperienceBarHeight,
            JobExperienceBarHeight + ExperienceBarHeight);
        m_bar.SetActive(false);
    }

    private RectTransform CreateStrip(string objectName, Color fill, float bottom, float top)
    {
        RectTransform fillRect = UiBuilder.CreateBar(objectName, m_bar!.transform, ExperienceBackColor, fill);
        GameObject strip = fillRect.parent.gameObject;
        strip.AddComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)strip.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(0f, bottom);
        rect.offsetMax = new Vector2(0f, top);
        return fillRect;
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
