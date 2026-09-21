using UnityEngine;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
/// Development-only readout and controls: connection state, prediction health, and the simulated link quality.
/// F1 hides it.
/// </summary>
public sealed class DevelopmentOverlay : MonoBehaviour
{
    private const float Width = 360f;
    private const float Margin = 10f;
    private const float ReferenceDpi = 110f;

    private GameClient? m_client;
    private Rect m_area = new Rect(Margin, Margin, Width, 0f);
    private bool m_isVisible = true;
    private string m_portText = string.Empty;
    private string m_characterText = string.Empty;

    private static float Scale => Mathf.Clamp(Screen.dpi <= 0f ? 1f : Screen.dpi / ReferenceDpi, 1f, 3f);

    private void Update()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
        {
            m_isVisible = !m_isVisible;
        }
    }

    private void OnGUI()
    {
        if (!m_isVisible || m_client == null)
        {
            return;
        }

        float scale = Scale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUILayout.BeginArea(new Rect(Margin, Margin, Width, (Screen.height / scale) - (2f * Margin)));
        GUILayout.BeginVertical(GUI.skin.box);
        DrawConnection(m_client);
        DrawWorld(m_client);
        DrawLink(m_client);
        GUILayout.EndVertical();
        if (Event.current.type == EventType.Repaint)
        {
            Rect drawn = GUILayoutUtility.GetLastRect();
            m_area = new Rect(Margin, Margin, Width, drawn.height);
        }

        GUILayout.EndArea();
    }

    public void Bind(GameClient client)
    {
        m_client = client;
        m_portText = client.Port.ToString();
    }

    /// <summary>
    /// Whether a screen position (origin bottom-left, as the input system reports it) is on the overlay, so that
    /// using its controls is never also a click on the ground.
    /// </summary>
    public bool Covers(Vector2 screenPosition)
    {
        if (!m_isVisible)
        {
            return false;
        }

        float scale = Scale;
        Vector2 guiPosition = new Vector2(screenPosition.x / scale, (Screen.height - screenPosition.y) / scale);
        return m_area.Contains(guiPosition);
    }

    private void DrawConnection(GameClient client)
    {
        ClientConnection? connection = client.Connection;
        GUILayout.Label(client.Status);
        bool isOpen = connection != null && connection.State != ClientConnectionState.Disconnected;
        if (connection != null)
        {
            GUILayout.Label(
                "State " + connection.State + "   RTT " + connection.RoundTripMilliseconds + " ms   Server "
                + connection.ServerBuildVersion);
            GUILayout.Label(
                "Malformed " + connection.MalformedMessages + "   Unexpected " + connection.UnexpectedMessages);
        }

        if (isOpen)
        {
            if (GUILayout.Button("Disconnect"))
            {
                client.Disconnect();
            }

            return;
        }

        if (m_characterText.Length == 0)
        {
            m_characterText = client.Character.ToString();
        }

        client.Host = LabelledField("Host", client.Host);
        m_portText = LabelledField("Port", m_portText);
        client.Identity = LabelledField("Identity", client.Identity);
        m_characterText = LabelledField("Character", m_characterText);
        if (GUILayout.Button("Connect")
            && int.TryParse(m_portText, out int port)
            && long.TryParse(m_characterText, out long character))
        {
            client.Port = port;
            client.Character = character;
            client.Connect();
        }
    }

    private static void DrawWorld(GameClient client)
    {
        ClientWorld? world = client.World;
        if (world == null)
        {
            return;
        }

        MovementPredictor predictor = world.Predictor;
        RenderSmoother smoother = world.Smoother;
        GUILayout.Space(6f);
        GUILayout.Label(
            "Tick " + world.LatestServerTick + "   Pos " + predictor.Position.X.ToString("F2") + ", "
            + predictor.Position.Y.ToString("F2") + ", " + predictor.Position.Z.ToString("F2"));
        GUILayout.Label(
            "Pending " + predictor.PendingCount + "   Ack " + predictor.LastAcknowledgedSequence + "   Dropped "
            + predictor.DroppedPendingInputs);
        GUILayout.Label(
            "Correction last " + smoother.LastCorrection.ToString("F3") + " m   max "
            + smoother.LargestCorrection.ToString("F3") + " m   snaps " + smoother.Snaps);
        GUILayout.Label(
            "Snapshots " + world.SnapshotsApplied + "   stale " + world.StaleSnapshots + "   unknown states "
            + world.UnknownEntityStates + "   remotes " + world.Remotes.Count);
        MovementController? controller = client.Controller;
        if (controller != null)
        {
            GUILayout.Label(
                "Walk " + (controller.HasPath ? "active" : "none") + "   refused clicks "
                + controller.RejectedMoveRequests + "   cancelled " + controller.CancelledPaths);
        }

        if (client.Clock != null && client.Clock.SkippedTicks > 0)
        {
            GUILayout.Label("Skipped client ticks " + client.Clock.SkippedTicks);
        }
    }

    private static void DrawLink(GameClient client)
    {
        LossyTransport? link = client.Link;
        if (link == null)
        {
            return;
        }

        GUILayout.Space(6f);
        GUILayout.Label("Simulated link (each way)   dropped " + link.Dropped + "   reordered " + link.Reordered);
        link.LatencyMilliseconds = LabelledSlider("Latency ms", link.LatencyMilliseconds, 300);
        link.JitterMilliseconds = LabelledSlider("Jitter ms", link.JitterMilliseconds, 100);
        link.LossPercent = LabelledSlider("Loss %", link.LossPercent, 30);
        link.ReorderPercent = LabelledSlider("Reorder %", link.ReorderPercent, 30);

        TouchControls? touch = client.Touch;
        if (touch != null)
        {
            bool isShown = GUILayout.Toggle(touch.IsVisible, " On-screen stick");
            if (isShown != touch.IsVisible)
            {
                touch.SetVisible(isShown);
            }
        }
    }

    private static string LabelledField(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90f));
        string edited = GUILayout.TextField(value, 64);
        GUILayout.EndHorizontal();
        return edited;
    }

    private static int LabelledSlider(string label, int value, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + " " + value, GUILayout.Width(120f));
        int edited = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, 0f, max));
        GUILayout.EndHorizontal();
        return edited;
    }
}
}
