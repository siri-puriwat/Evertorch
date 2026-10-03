using System;
using System.Globalization;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The confirmed target (Prototype Content §2, §4): a monster's name, its HP ratio as a bar, its distance, and
///     whether it is dead, or a selected player's name and job, "Ann · Vanguard", with its HP bar only for a party
///     member, since others' HP is not shown, and Invite while the player could join the party (Prototype Content §2). It
///     shows only a target the server confirmed with <c>TargetChanged</c>, and the HP that the monster's own bar
///     shows at that moment, so the frame never runs ahead of the hits on screen. It sits at the top
///     centre, and narrower beside a window at the top left while one shows (finding 1 of the Milestone 7 review).
/// </summary>
public sealed class TargetFrame : MonoBehaviour
{
    public const string Invite = "Invite";

    /// <summary>
    ///     The frame at its tallest, a selected player's name, its empty detail line, and Invite, in canvas units.
    /// </summary>
    public const float TallestHeight = 2 * Padding + 3 * RowHeight + 2 * Spacing;

    // With the status bar, under the chat and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;

    // Between the windows at the top left and the inventory window at the top right.
    private const float BesideWidth = 320f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const float BarHeight = 14f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float Spacing = 6f;

    // The colours of the bar over the monster.
    private static readonly Color BarBackColor = new(0.15f, 0.05f, 0.05f);
    private static readonly Color BarFillColor = new(0.85f, 0.2f, 0.2f);

    private static readonly UiBuilder Ui = new(22f, RowHeight, 0f, Spacing);

    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_name;
    private RectTransform? m_fill;
    private TMP_Text? m_detail;
    private GameObject? m_invite;
    private string m_targetName = string.Empty;
    private ClientMonster? m_labelled;
    private string? m_labelledId;
    private string m_label = string.Empty;
    private string? m_shownName;
    private int m_shownPermille = -1;
    private int m_shownTenths = -1;
    private bool m_isShownPlayer;
    private bool m_shownDead;
    private RectTransform? m_panelRect;
    private bool m_isShownBeside;
    private bool m_isShownBesideTouch;

    /// <summary>
    ///     How many times a label was rewritten, which only a changed value may cause.
    /// </summary>
    public int TextChanges { get; private set; }

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The filled part of the bar, from 0 to 1.
    /// </summary>
    public float ShownRatio => m_fill != null ? m_fill.anchorMax.x : 0f;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (m_client == null
            || world == null
            || world.Target == default
            || !world.Remotes.TryGetValue(world.Target, out RemoteEntity? target))
        {
            Hide();
            return;
        }

        PlaceBeside(m_client.IsSideWindowOpen, m_client.Touch != null && m_client.Touch.IsVisible);
        if (target.Kind == EntityKind.Player)
        {
            int? health = m_client.Party.TryGetMember(target.Name, out PartyMember? member)
                ? member!.HealthPermille
                : null;
            ShowPlayer(
                PlayerLabel(target.Name,
                    BuildMessages.JobName(m_client.Content, new JobDefinitionId(target.DefinitionId))),
                health / 1000f);
            m_targetName = target.Name;
            UiBuilder.SetActive(m_invite!, CanInvite(m_client, target.Name));
            return;
        }

        UiBuilder.SetActive(m_invite!, false);

        float ratio = target.HealthPermille / 1000f;
        bool isDead = target.IsDead;
        CombatPresenter? combat = m_client.Combat;
        if (combat != null)
        {
            if (combat.TryGetHealthBar(target.Entity, out HealthBar? bar) && bar != null)
            {
                ratio = bar.ShownRatio;
            }

            isDead = combat.Timeline.IsShownDead(
                target.Entity,
                target.IsDead,
                world.ServerTime.Now,
                world.RemoteRenderTime);
        }

        float? distance = null;
        if (m_client.RemoteViews.TryGetValue(target.Entity, out EntityView? view) && view != null)
        {
            WorldPosition self = world.Predictor.Position;
            Vector3 at = view.transform.position;
            distance = new Vector2(at.x - self.X, at.z - self.Z).magnitude;
        }

        ShowTarget(
            CachedMonsterLabel(
                EntityViewKeys.MonsterOf(m_client.Content, target.Kind, target.DefinitionId),
                target.DefinitionId),
            ratio,
            distance,
            isDead);
    }

    /// <summary>
    ///     The frame's left and right edges in canvas units: centred, or just right of the windows at the top left, which
    ///     stand further right while the touch controls show, and then short of the inventory window's place.
    /// </summary>
    public static Vector2 XRangeFor(bool isBesideAWindow, bool isTouchShown = false)
    {
        if (!isBesideAWindow)
        {
            return new Vector2((ClientUI.CanvasWidth - Width) / 2f, (ClientUI.CanvasWidth + Width) / 2f);
        }

        float left = NpcWindow.RightFor(isTouchShown) + Margin;
        return new Vector2(left, Math.Min(left + BesideWidth, InventoryWindow.Left - Margin));
    }

    /// <summary>
    ///     The lowest the frame at its tallest reaches on a canvas <paramref name="canvasHeight" /> units tall, from the
    ///     bottom.
    /// </summary>
    public static float BottomFor(float canvasHeight)
    {
        return canvasHeight - (StatusBarHeight + Margin) - TallestHeight;
    }

    /// <summary>
    ///     Whether Invite shows for the player named <paramref name="name" />: someone else, not already a member, while
    ///     the player has no party or leads it.
    /// </summary>
    public static bool CanInvite(GameClient client, string name)
    {
        string? own = client.PlayedCharacter?.Name;
        ClientParty party = client.Party;
        return CharacterNames.IsValid(name)
            && !string.Equals(name, own, StringComparison.OrdinalIgnoreCase)
            && !party.TryGetMember(name, out _)
            && (!party.IsInParty || party.IsLeader(own));
    }

    public static TargetFrame Create(GameClient client)
    {
        var root = new GameObject("TargetFrame", typeof(RectTransform));
        root.SetActive(false);
        TargetFrame frame = root.AddComponent<TargetFrame>();
        frame.Build(client);
        root.SetActive(true);
        return frame;
    }

    /// <param name="name">The name to show.</param>
    /// <param name="ratio">The HP ratio, from 0 to 1.</param>
    /// <param name="distanceMeters">Null while the target has no view to measure to.</param>
    /// <param name="isDead">Whether the target is shown dead.</param>
    public void ShowTarget(string name, float ratio, float? distanceMeters, bool isDead)
    {
        UiBuilder.SetActive(m_panel!, true);
        ShowName(name);
        if (m_isShownPlayer)
        {
            m_isShownPlayer = false;
            UiBuilder.SetActive(m_fill!.parent.gameObject, true);
        }

        int permille = isDead ? 0 : Mathf.RoundToInt(Mathf.Clamp01(ratio) * 1000f);
        if (permille != m_shownPermille)
        {
            m_shownPermille = permille;
            m_fill!.anchorMax = new Vector2(permille / 1000f, 1f);
        }

        int tenths = distanceMeters.HasValue ? Mathf.RoundToInt(distanceMeters.Value * 10f) : -1;
        if (tenths == m_shownTenths && isDead == m_shownDead)
        {
            return;
        }

        m_shownTenths = tenths;
        m_shownDead = isDead;
        string detail = tenths < 0 ? string.Empty : $"{(tenths / 10f).ToString("F1", CultureInfo.InvariantCulture)} m";
        if (isDead)
        {
            detail = detail.Length == 0 ? "Dead" : $"{detail}   Dead";
        }

        Write(m_detail!, detail);
    }

    /// <summary>
    ///     A selected player's name and job, "Ann · Vanguard"; the job alone if the player has no name.
    /// </summary>
    public static string PlayerLabel(string name, string jobName)
    {
        return string.IsNullOrEmpty(name) ? jobName : $"{name} \u00B7 {jobName}";
    }

    /// <summary>
    ///     A selected player: <paramref name="label" />, without a distance, and with the HP bar at
    ///     <paramref name="ratio" /> for a party member whose status has come, or no bar.
    /// </summary>
    public void ShowPlayer(string label, float? ratio = null)
    {
        UiBuilder.SetActive(m_panel!, true);
        ShowName(label);
        UiBuilder.SetActive(m_fill!.parent.gameObject, ratio.HasValue);
        if (ratio.HasValue)
        {
            int permille = Mathf.RoundToInt(Mathf.Clamp01(ratio.Value) * 1000f);
            if (permille != m_shownPermille)
            {
                m_shownPermille = permille;
                m_fill.anchorMax = new Vector2(permille / 1000f, 1f);
            }
        }

        if (m_isShownPlayer)
        {
            return;
        }

        m_isShownPlayer = true;
        m_shownTenths = -1;
        m_shownDead = false;
        Write(m_detail!, string.Empty);
    }

    public void Hide()
    {
        UiBuilder.SetActive(m_panel!, false);
    }

    private void ShowName(string name)
    {
        if (!string.Equals(name, m_shownName, StringComparison.Ordinal))
        {
            m_shownName = name;
            Write(m_name!, name);
        }
    }

    /// <summary>
    ///     A selected monster's name and level, "Grotto Crawler · Lv 14", and a boss named one, "Slime Monarch · Lv 25 ·
    ///     Boss"; the definition ID when the content does not know the monster.
    /// </summary>
    public static string MonsterLabel(ClientMonster? monster, string definitionId)
    {
        if (monster == null)
        {
            return definitionId;
        }

        string label = $"{monster.DisplayName} \u00B7 Lv {monster.Level.ToString(CultureInfo.InvariantCulture)}";
        return monster.IsBoss ? $"{label} \u00B7 Boss" : label;
    }

    // The frame asks for its monster's label every frame; it is built once for each monster it names.
    private string CachedMonsterLabel(ClientMonster? monster, string definitionId)
    {
        if (!ReferenceEquals(monster, m_labelled) ||
            !string.Equals(definitionId, m_labelledId, StringComparison.Ordinal))
        {
            m_labelled = monster;
            m_labelledId = definitionId;
            m_label = MonsterLabel(monster, definitionId);
        }

        return m_label;
    }

    private void PlaceBeside(bool isBeside, bool isTouchShown)
    {
        if (isBeside == m_isShownBeside && isTouchShown == m_isShownBesideTouch)
        {
            return;
        }

        m_isShownBeside = isBeside;
        m_isShownBesideTouch = isTouchShown;
        Vector2 range = XRangeFor(isBeside, isTouchShown);
        m_panelRect!.sizeDelta = new Vector2(range.y - range.x, m_panelRect.sizeDelta.y);
        m_panelRect.anchoredPosition = new Vector2(
            (range.x + range.y) / 2f - ClientUI.CanvasWidth / 2f,
            m_panelRect.anchoredPosition.y);
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel = Ui.CreatePanel(
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0f, -(StatusBarHeight + Margin)),
            Width,
            Padding);
        m_panel = panel.gameObject;
        m_panelRect = panel;
        m_name = Ui.CreateLabel("Name", panel);
        m_name.alignment = TextAlignmentOptions.Center;

        m_fill = UiBuilder.CreateBar("Bar", panel, BarBackColor, BarFillColor);
        m_fill.parent.gameObject.AddComponent<LayoutElement>().preferredHeight = BarHeight;

        m_detail = Ui.CreateLabel("Detail", panel);
        m_detail.alignment = TextAlignmentOptions.Center;
        m_invite = Ui.CreateButton(Invite, panel, () => client.InviteToParty(m_targetName));
        m_invite.SetActive(false);
        m_panel.SetActive(false);
    }

    private void Write(TMP_Text label, string text)
    {
        label.text = text;
        TextChanges++;
    }
}
}
