using Evertorch.Game;
using TMPro;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client
{
/// <summary>
///     The window of the NPC the player walked up to (Gameplay Systems §6.1; Prototype Content §2): the NPC's name and
///     Close. It sends nothing itself, and it closes once the NPC is drawn beyond its range, the player dies, the map
///     changes, or the connection closes.
/// </summary>
public sealed class NpcWindow : MonoBehaviour
{
    // With the status bar and the target frame, under the feedback lines and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;

    private static readonly UiBuilder Ui = new(22f, 30f, 0f, 6f);

    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_name;
    private ClientWorld? m_world;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The NPC the window is open for; default while it is closed.
    /// </summary>
    public EntityId Npc { get; private set; }

    public string ShownName => m_name != null ? m_name.text : string.Empty;

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        ClientWorld? world = m_client != null ? m_client.World : null;
        if (m_client == null
            || world == null
            || world != m_world
            || world.IsLocalDead
            || !m_client.RemoteViews.TryGetValue(Npc, out EntityView? view)
            || view == null)
        {
            Close();
            return;
        }

        // Measured as the target frame measures: from the predicted player to where the NPC is drawn.
        WorldPosition self = world.Predictor.Position;
        Vector3 at = view.transform.position;
        if (new Vector2(at.x - self.X, at.z - self.Z).magnitude > NpcInteraction.Range)
        {
            Close();
        }
    }

    public static NpcWindow Create(GameClient client)
    {
        var root = new GameObject("NpcWindow", typeof(RectTransform));
        root.SetActive(false);
        NpcWindow window = root.AddComponent<NpcWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    /// <summary>
    ///     Opens the window for <paramref name="npc" /> in the current world, showing its name.
    /// </summary>
    public void Open(EntityId npc)
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (world == null || !world.Remotes.TryGetValue(npc, out RemoteEntity? remote))
        {
            return;
        }

        m_world = world;
        Npc = npc;
        m_name!.text = NpcName(m_client!.Content, remote.DefinitionId);
        UiBuilder.SetActive(m_panel!, true);
    }

    public void Close()
    {
        m_world = null;
        Npc = default;
        UiBuilder.SetActive(m_panel!, false);
    }

    private static string NpcName(ClientContent? content, string definitionId)
    {
        return content != null
            && NpcDefinitionId.TryCreate(definitionId, out NpcDefinitionId id)
            && content.TryGetNpc(id, out ClientNpc? npc)
            && npc != null
                ? npc.DisplayName
                : definitionId;
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel = Ui.CreatePanel(
            transform,
            new Vector2(0f, 1f),
            new Vector2(Margin, -(StatusBarHeight + Margin)),
            Width,
            Padding);
        m_panel = panel.gameObject;
        m_name = Ui.CreateLabel("Name", panel);
        m_name.alignment = TextAlignmentOptions.Center;
        Ui.CreateButton("Close", panel, Close);
        m_panel.SetActive(false);
    }
}
}
