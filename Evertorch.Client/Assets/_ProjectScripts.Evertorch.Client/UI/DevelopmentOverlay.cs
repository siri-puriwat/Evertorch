using System;
using Evertorch.Game;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     Development-only readout and controls: connection and prediction telemetry, the simulated link quality, and the
///     on-screen stick. F1 or the touch toggle shows and hides it; the panels a player sees do the rest (Prototype
///     Content §2).
/// </summary>
public sealed class DevelopmentOverlay : MonoBehaviour
{
    // Drawn over every other client canvas.
    private const int SortingOrder = 10;
    private const float Width = 380f;
    private const float Margin = 10f;
    private const int Padding = 8;

    private static readonly UiBuilder Ui = new(14f, 26f, 120f, 4f);

    private View? m_view;

    public RectTransform? Panel { get; private set; }

    public bool IsVisible => Panel != null && Panel.gameObject.activeSelf;

    private void Update()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
        {
            Toggle();
        }

        if (IsVisible)
        {
            m_view?.Refresh();
        }
    }

    /// <summary>
    ///     Builds the overlay hidden; F1 or the touch toggle shows it.
    /// </summary>
    public static DevelopmentOverlay Create(GameClient client)
    {
        // Built inactive, so every control is fully wired before any of them is enabled.
        var root = new GameObject("DevelopmentOverlay", typeof(RectTransform));
        root.SetActive(false);
        DevelopmentOverlay overlay = root.AddComponent<DevelopmentOverlay>();
        overlay.Build(client);
        root.SetActive(true);
        return overlay;
    }

    public void Toggle()
    {
        if (Panel != null)
        {
            Panel.gameObject.SetActive(!Panel.gameObject.activeSelf);
        }
    }

    private void Build(GameClient client)
    {
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        Panel = Ui.CreatePanel(transform, new Vector2(0f, 1f), new Vector2(Margin, -Margin), Width, Padding);
        m_view = new View(client, Panel, Toggle);
        m_view.Refresh();
        Panel.gameObject.SetActive(false);
    }

    private sealed class View
    {
        private readonly GameClient m_client;
        private readonly TMP_Text m_connection;
        private readonly GameObject m_disconnect;
        private readonly GameObject m_respawn;
        private readonly GameObject m_logout;
        private readonly TMP_Text m_world;
        private readonly GameObject m_link;
        private readonly TMP_Text m_linkCounters;
        private readonly LinkSlider[] m_linkSliders;
        private readonly Toggle m_stick;

        public View(GameClient client, Transform panel, UnityAction hide)
        {
            m_client = client;

            // The touch toggle may be under the open panel, so the panel closes itself too.
            Ui.CreateButton("Hide", panel, hide);
            m_connection = Ui.CreateLabel("Connection", panel);
            m_disconnect = Ui.CreateButton("Disconnect", panel, client.Disconnect);
            m_respawn = Ui.CreateButton("Respawn", panel, client.RequestRespawn);
            m_logout = Ui.CreateButton("Logout", panel, client.Logout);
            m_world = Ui.CreateLabel("World", panel);

            m_link = Ui.CreateColumn("Link", panel);
            Transform link = m_link.transform;
            m_linkCounters = Ui.CreateLabel("Counters", link);
            m_linkSliders = new[]
            {
                new LinkSlider(
                    link,
                    "Latency ms",
                    300,
                    client,
                    transport => transport.LatencyMilliseconds,
                    (transport, value) => transport.LatencyMilliseconds = value),
                new LinkSlider(
                    link,
                    "Jitter ms",
                    100,
                    client,
                    transport => transport.JitterMilliseconds,
                    (transport, value) => transport.JitterMilliseconds = value),
                new LinkSlider(
                    link,
                    "Loss %",
                    30,
                    client,
                    transport => transport.LossPercent,
                    (transport, value) => transport.LossPercent = value),
                new LinkSlider(
                    link,
                    "Reorder %",
                    30,
                    client,
                    transport => transport.ReorderPercent,
                    (transport, value) => transport.ReorderPercent = value)
            };

            m_stick = Ui.CreateToggle(panel, "On-screen stick", isShown => client.Touch?.SetVisible(isShown));
        }

        public void Refresh()
        {
            ClientConnection? connection = m_client.Connection;
            bool isOpen = connection != null && connection.State != ClientConnectionState.Disconnected;
            UiBuilder.SetText(
                m_connection,
                connection == null
                    ? m_client.Status
                    : $"{m_client.Status}\nState {connection.State}   RTT {connection.RoundTripMilliseconds} ms"
                    + $"   Server {connection.ServerBuildVersion}"
                    + $"\nMalformed {connection.MalformedMessages}   Unexpected {connection.UnexpectedMessages}");
            UiBuilder.SetActive(m_disconnect, isOpen);
            UiBuilder.SetActive(m_respawn, m_client.World?.IsLocalDead == true);
            UiBuilder.SetActive(m_logout, connection != null && connection.State == ClientConnectionState.InWorld);
            RefreshWorld();
            RefreshLink();

            TouchControls? touch = m_client.Touch;
            UiBuilder.SetActive(m_stick.gameObject, touch != null);
            if (touch != null)
            {
                m_stick.SetIsOnWithoutNotify(touch.IsVisible);
            }
        }

        private void RefreshWorld()
        {
            ClientWorld? world = m_client.World;
            UiBuilder.SetActive(m_world.gameObject, world != null);
            if (world == null)
            {
                return;
            }

            MovementPredictor predictor = world.Predictor;
            WorldPosition position = predictor.Position;
            RenderSmoother smoother = world.Smoother;
            string text = $"Tick {world.LatestServerTick}   Pos {position.X:F2}, {position.Y:F2}, {position.Z:F2}"
                + $"\nPending {predictor.PendingCount}   Ack {predictor.LastAcknowledgedSequence}"
                + $"   Dropped {predictor.DroppedPendingInputs}"
                + $"\nCorrection last {smoother.LastCorrection:F3} m   max {smoother.LargestCorrection:F3} m"
                + $"   snaps {smoother.Snaps}"
                + $"\nSnapshots {world.SnapshotsApplied}   stale {world.StaleSnapshots}"
                + $"   unknown states {world.UnknownEntityStates}   remotes {world.Remotes.Count}";
            MovementController? controller = m_client.Controller;
            if (controller != null)
            {
                text += $"\nWalk {(controller.HasPath ? "active" : "none")}"
                    + $"   refused clicks {controller.RejectedMoveRequests}   cancelled {controller.CancelledPaths}";
            }

            if (m_client.Clock != null && m_client.Clock.SkippedTicks > 0)
            {
                text += $"\nSkipped client ticks {m_client.Clock.SkippedTicks}";
            }

            UiBuilder.SetText(m_world, text);
        }

        private void RefreshLink()
        {
            LossyTransport? link = m_client.Link;
            UiBuilder.SetActive(m_link, link != null);
            if (link == null)
            {
                return;
            }

            UiBuilder.SetText(
                m_linkCounters,
                $"Simulated link (each way)   dropped {link.Dropped}   reordered {link.Reordered}");
            foreach (LinkSlider slider in m_linkSliders)
            {
                slider.Show(link);
            }
        }
    }

    private sealed class LinkSlider
    {
        private readonly string m_name;
        private readonly Func<LossyTransport, int> m_read;
        private readonly Slider m_slider;
        private readonly TMP_Text m_label;
        private int m_shown = -1;

        public LinkSlider(
            Transform parent,
            string name,
            int max,
            GameClient client,
            Func<LossyTransport, int> read,
            Action<LossyTransport, int> write)
        {
            m_name = name;
            m_read = read;
            GameObject row = Ui.CreateRow(name, parent);
            m_label = Ui.CreateRowLabel(name, row.transform);
            m_slider = Ui.CreateSlider(row.transform, max);
            m_slider.onValueChanged.AddListener(value =>
            {
                // Every connect replaces the link, so the current one is looked up on each change.
                LossyTransport? link = client.Link;
                if (link != null)
                {
                    write(link, Mathf.RoundToInt(value));
                }
            });
        }

        public void Show(LossyTransport link)
        {
            int value = m_read(link);
            if (value == m_shown)
            {
                return;
            }

            m_shown = value;
            m_slider.SetValueWithoutNotify(value);
            m_label.text = $"{m_name} {value}";
        }
    }
}
}
