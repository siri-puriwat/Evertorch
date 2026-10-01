using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The party list under the status bar at the left (Prototype Content §2): a row per member with its name, level,
///     and job, "(L)" for the leader, and "Offline" or the map's name when it is not beside the player. It offers
///     Leave, and the leader Kick and Lead on each other member's row, each armed by a first press. It hides while a
///     side window shows, and outside a party.
/// </summary>
public sealed class PartyList : MonoBehaviour
{
    public const float Width = 180f;
    public const float HeaderHeight = 24f;
    public const float RowHeight = 36f;

    /// <summary>
    ///     How long a first press keeps a button armed for the second.
    /// </summary>
    public const double ArmedSeconds = 3.0;

    public const string Leave = "Leave";
    public const string Kick = "Kick";
    public const string Lead = "Lead";
    public const string Confirm = "Sure?";

    // With the status bar and the target frame.
    private const int SortingOrder = 6;
    private const float StatusBarClearance = 64f;
    private const float Gap = 8f;
    private const float DesktopLeft = 8f;

    // Clear of the Dev, Stats, and Skills buttons at x 0 to 110.
    private const float TouchLeft = 118f;
    private const float ActionWidth = 40f;
    private const float FontSize = 14f;
    private const float Inset = 4f;

    private static readonly Color AwayColor = new(0.55f, 0.57f, 0.6f);
    private static readonly UiBuilder Ui = new(FontSize, HeaderHeight, 0f, 0f);

    private readonly List<Row> m_rows = new();
    private GameClient? m_client;
    private ClientParty? m_party;
    private GameObject? m_panel;
    private RectTransform? m_panelRect;
    private TMP_Text? m_leaveLabel;
    private bool m_isChanged = true;
    private bool? m_isTouchLayout;
    private float m_placedHeight = -1f;
    private string? m_armed;
    private double m_armedUntil;

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The rows' text as shown, one member per line, for tests and the overlay.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    public RectTransform? Panel => m_panelRect;

    private void Update()
    {
        if (m_client == null || m_party == null)
        {
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;
        if (m_armed != null && now >= m_armedUntil)
        {
            Disarm();
        }

        bool isShown = m_client.IsInWorld && m_party.IsInParty && !m_client.IsSideWindowOpen;
        UiBuilder.SetActive(m_panel!, isShown);
        if (!isShown)
        {
            return;
        }

        bool isTouch = m_client.Touch != null && m_client.Touch.IsVisible;
        float canvasHeight = ((RectTransform)transform).rect.height;
        if (m_isChanged || isTouch != m_isTouchLayout || !Mathf.Approximately(canvasHeight, m_placedHeight))
        {
            m_isChanged = false;
            Rebuild(isTouch, canvasHeight);
        }
    }

    private void OnDestroy()
    {
        if (m_party != null)
        {
            m_party.Changed -= OnPartyChanged;
        }
    }

    public static PartyList Create(GameClient client)
    {
        var root = new GameObject("PartyList", typeof(RectTransform));
        root.SetActive(false);
        PartyList list = root.AddComponent<PartyList>();
        list.Build(client);
        root.SetActive(true);
        return list;
    }

    /// <summary>
    ///     The list's left edge: at the margin, or clear of the touch window buttons while they show.
    /// </summary>
    public static float LeftFor(bool isTouchShown)
    {
        return isTouchShown ? TouchLeft : DesktopLeft;
    }

    /// <summary>
    ///     The lowest the list reaches: above the chat, and with the touch controls above the stick as well.
    /// </summary>
    public static float FloorFor(float canvasHeight, bool isTouchShown)
    {
        float below = ChatPanel.TopFor(canvasHeight, isTouchShown);
        if (isTouchShown)
        {
            below = Math.Max(below, TouchControls.StickBounds.yMax);
        }

        return below + Gap;
    }

    /// <summary>
    ///     A row's height: 36, or less on a screen too short for five such rows above the floor.
    /// </summary>
    public static float RowHeightFor(float canvasHeight, bool isTouchShown)
    {
        float room = canvasHeight - StatusBarClearance - FloorFor(canvasHeight, isTouchShown) - HeaderHeight;
        return Math.Min(RowHeight, room / PartyRoster.MaxMembers);
    }

    /// <summary>
    ///     Where the list of <paramref name="members" /> sits on a canvas <paramref name="canvasHeight" /> units tall,
    ///     from the bottom-left corner: just under the status bar, from x 8 on the desktop or x 118 with the touch
    ///     controls, a header of 24 and a row each.
    /// </summary>
    public static Rect BoundsFor(float canvasHeight, bool isTouchShown, int members = PartyRoster.MaxMembers)
    {
        float top = canvasHeight - StatusBarClearance;
        float height = HeaderHeight + members * RowHeightFor(canvasHeight, isTouchShown);
        return new Rect(LeftFor(isTouchShown), top - height, Width, height);
    }

    /// <summary>
    ///     Presses the list's button <paramref name="action" />, for <paramref name="member" /> on a row: the first
    ///     press arms it, and the second within <see cref="ArmedSeconds" /> acts.
    /// </summary>
    public void Press(string action, string member)
    {
        string key = $"{action} {member}";
        double now = Time.realtimeSinceStartupAsDouble;
        if (!string.Equals(m_armed, key, StringComparison.Ordinal) || now >= m_armedUntil)
        {
            m_armed = key;
            m_armedUntil = now + ArmedSeconds;
            m_isChanged = true;
            return;
        }

        Disarm();
        switch (action)
        {
            case Leave:
                m_client!.LeaveParty();
                break;
            case Kick:
                m_client!.KickFromParty(member);
                break;
            case Lead:
                m_client!.MakePartyLeader(member);
                break;
        }
    }

    private void Disarm()
    {
        m_armed = null;
        m_isChanged = true;
    }

    private void OnPartyChanged()
    {
        m_isChanged = true;
    }

    private void Build(GameClient client)
    {
        m_client = client;
        m_party = client.Party;
        m_party.Changed += OnPartyChanged;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        GameObject panel = UiBuilder.CreateUiObject("Panel", transform);
        m_panel = panel;
        m_panelRect = (RectTransform)panel.transform;
        m_panelRect.anchorMin = Vector2.zero;
        m_panelRect.anchorMax = Vector2.zero;
        m_panelRect.pivot = Vector2.zero;
        panel.AddComponent<Image>().color = UiBuilder.PanelColor;

        TMP_Text title = Ui.CreateLabel("Title", panel.transform);
        title.text = "Party";
        Place(title.rectTransform, Inset, Width - ActionWidth - Inset, 0f, HeaderHeight);
        GameObject leave = Ui.CreateButton(Leave, panel.transform, () => Press(Leave, string.Empty));
        Place((RectTransform)leave.transform, Width - ActionWidth - Inset, ActionWidth, 2f, HeaderHeight - 4f);
        m_leaveLabel = leave.GetComponentInChildren<TMP_Text>();
        m_leaveLabel.fontSize = FontSize;
        panel.SetActive(false);
    }

    private void Rebuild(bool isTouch, float canvasHeight)
    {
        m_isTouchLayout = isTouch;
        m_placedHeight = canvasHeight;
        IReadOnlyList<PartyMember> members = m_party!.Members;
        float rowHeight = RowHeightFor(canvasHeight, isTouch);
        Rect bounds = BoundsFor(canvasHeight, isTouch, members.Count);
        m_panelRect!.anchoredPosition = bounds.position;
        m_panelRect.sizeDelta = bounds.size;

        while (m_rows.Count < members.Count)
        {
            m_rows.Add(CreateRow(m_rows.Count));
        }

        string? own = m_client!.PlayedCharacter?.Name;
        bool isLeader = m_party.IsLeader(own);
        MapDefinitionId? ownMap = m_client.World?.Map;
        ClientContent? content = m_client.Content;
        var text = new StringBuilder();
        for (int index = 0; index < m_rows.Count; index++)
        {
            Row row = m_rows[index];
            bool isUsed = index < members.Count;
            UiBuilder.SetActive(row.Root, isUsed);
            if (!isUsed)
            {
                continue;
            }

            PartyMember member = members[index];
            float top = HeaderHeight + index * rowHeight;
            Place(row.Root.GetComponent<RectTransform>(), 0f, Width, top, rowHeight);
            string name = PartyMessages.Row(member, content);
            string where = PartyMessages.Whereabouts(member, ownMap, content);
            UiBuilder.SetText(row.Name, name);
            UiBuilder.SetText(row.Where, where);
            row.Name.color = member.IsInWorld ? UiBuilder.TextColor : AwayColor;
            bool isOther = !string.Equals(member.Name, own, StringComparison.OrdinalIgnoreCase);
            bool hasActions = isLeader && isOther;
            UiBuilder.SetActive(row.Kick, hasActions);
            UiBuilder.SetActive(row.Lead, hasActions);
            row.Member = member.Name;
            UiBuilder.SetText(row.KickLabel, IsArmed(Kick, member.Name) ? Confirm : Kick);
            UiBuilder.SetText(row.LeadLabel, IsArmed(Lead, member.Name) ? Confirm : Lead);
            text.Append(where.Length == 0 ? name : $"{name}  {where}").Append('\n');
        }

        UiBuilder.SetText(m_leaveLabel!, IsArmed(Leave, string.Empty) ? Confirm : Leave);
        Text = text.ToString().TrimEnd('\n');
    }

    private bool IsArmed(string action, string member)
    {
        return string.Equals(m_armed, $"{action} {member}", StringComparison.Ordinal);
    }

    private Row CreateRow(int index)
    {
        GameObject root = UiBuilder.CreateUiObject($"Row{index + 1}", m_panel!.transform);
        var row = new Row(root);
        row.Name = Ui.CreateLabel("Name", root.transform);
        row.Name.richText = false;
        row.Name.textWrappingMode = TextWrappingModes.NoWrap;
        row.Name.overflowMode = TextOverflowModes.Ellipsis;
        StretchTop(row.Name.rectTransform, Inset, Width - 2f * ActionWidth - Inset);
        row.Where = Ui.CreateLabel("Where", root.transform);
        row.Where.richText = false;
        row.Where.color = AwayColor;
        row.Where.textWrappingMode = TextWrappingModes.NoWrap;
        row.Where.overflowMode = TextOverflowModes.Ellipsis;
        StretchBottom(row.Where.rectTransform, Inset, Width - 2f * ActionWidth - Inset);
        row.Kick = Ui.CreateButton(Kick, root.transform, () => Press(Kick, row.Member));
        row.KickLabel = row.Kick.GetComponentInChildren<TMP_Text>();
        row.Lead = Ui.CreateButton(Lead, root.transform, () => Press(Lead, row.Member));
        row.LeadLabel = row.Lead.GetComponentInChildren<TMP_Text>();
        StretchRight((RectTransform)row.Kick.transform, 2f * ActionWidth);
        StretchRight((RectTransform)row.Lead.transform, ActionWidth);
        return row;
    }

    // A child from the panel's top-left corner, x and width across, top and height down.
    private static void Place(RectTransform rect, float x, float width, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void StretchTop(RectTransform rect, float x, float width)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(x, 0f);
        rect.offsetMax = new Vector2(x + width, 0f);
    }

    private static void StretchBottom(RectTransform rect, float x, float width)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0f);
        rect.offsetMin = new Vector2(x, 0f);
        rect.offsetMax = new Vector2(x + width, 0f);
    }

    // A button of the actions' width ending <paramref name="fromRight" /> short of the right edge, as tall as the row.
    private static void StretchRight(RectTransform rect, float fromRight)
    {
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(-fromRight, 2f);
        rect.offsetMax = new Vector2(-fromRight + ActionWidth - 2f, -2f);
        TMP_Text label = rect.GetComponentInChildren<TMP_Text>();
        label.fontSize = FontSize - 2f;
    }

    private sealed class Row
    {
        public Row(GameObject root)
        {
            Root = root;
        }

        public GameObject Root { get; }

        public TMP_Text Name { get; set; } = null!;

        public TMP_Text Where { get; set; } = null!;

        public GameObject Kick { get; set; } = null!;

        public TMP_Text KickLabel { get; set; } = null!;

        public GameObject Lead { get; set; } = null!;

        public TMP_Text LeadLabel { get; set; } = null!;

        public string Member { get; set; } = string.Empty;
    }
}
}
