using System;
using System.Globalization;
using Evertorch.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The confirmed target (Prototype Content §2, §4): its name, its HP ratio as a bar, its distance, and whether it
///     is dead. It shows only a target the server confirmed with <c>TargetChanged</c>, and the HP that the monster's
///     own bar shows at that moment, so the frame never runs ahead of the hits on screen. It sits at the top centre, and
///     narrower beside a window at the top left while one shows (finding 1 of the Milestone 7 review).
/// </summary>
public sealed class TargetFrame : MonoBehaviour
{
    // With the status bar, under the feedback lines and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;

    // Between the windows at the top left and the inventory window at the top right.
    private const float BesideWidth = 320f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const float BarHeight = 14f;
    private const int Padding = 10;

    // The colours of the bar over the monster.
    private static readonly Color BarBackColor = new(0.15f, 0.05f, 0.05f);
    private static readonly Color BarFillColor = new(0.85f, 0.2f, 0.2f);

    private static readonly UiBuilder Ui = new(22f, 30f, 0f, 6f);

    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_name;
    private RectTransform? m_fill;
    private TMP_Text? m_detail;
    private string? m_shownName;
    private int m_shownPermille = -1;
    private int m_shownTenths = -1;
    private bool m_shownDead;
    private RectTransform? m_panelRect;
    private bool m_isShownBeside;

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

        PlaceBeside(m_client.IsSideWindowOpen);
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

        ShowTarget(MonsterName(m_client.Content, target), ratio, distance, isDead);
    }

    /// <summary>
    ///     The frame's left and right edges in canvas units: centred, or just right of the windows at the top left.
    /// </summary>
    public static Vector2 XRangeFor(bool isBesideAWindow)
    {
        return isBesideAWindow
            ? new Vector2(NpcWindow.Right + Margin, NpcWindow.Right + Margin + BesideWidth)
            : new Vector2((ClientUI.CanvasWidth - Width) / 2f, (ClientUI.CanvasWidth + Width) / 2f);
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
        if (!string.Equals(name, m_shownName, StringComparison.Ordinal))
        {
            m_shownName = name;
            Write(m_name!, name);
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

    public void Hide()
    {
        UiBuilder.SetActive(m_panel!, false);
    }

    private static string MonsterName(ClientContent? content, RemoteEntity target)
    {
        return content != null
            && content.TryGetMonster(new MonsterDefinitionId(target.DefinitionId), out ClientMonster? monster)
            && monster != null
                ? monster.DisplayName
                : target.DefinitionId;
    }

    private void PlaceBeside(bool isBeside)
    {
        if (isBeside == m_isShownBeside)
        {
            return;
        }

        m_isShownBeside = isBeside;
        Vector2 range = XRangeFor(isBeside);
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
        m_panel.SetActive(false);
    }

    private void Write(TMP_Text label, string text)
    {
        label.text = text;
        TextChanges++;
    }
}
}
