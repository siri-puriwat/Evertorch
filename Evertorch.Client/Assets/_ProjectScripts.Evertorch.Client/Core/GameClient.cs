using System.Collections;
using System.Collections.Generic;
using Evertorch.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client
{
/// <summary>
/// The client's composition root. It loads content, owns the connection, runs the client simulation at the server's
/// tick rate, and keeps the views in step with it. All state lives on this instance; nothing is static, because the
/// editor may keep the domain loaded between play sessions.
/// </summary>
public sealed class GameClient : MonoBehaviour
{
    private const string ConnectionKey = "evertorch";
    private const int DisconnectTimeoutMilliseconds = 10000;
    private const string DevelopmentTokenPrefix = "dev:";

    private static readonly Color LocalColor = new Color(0.25f, 0.65f, 0.95f);
    private static readonly Color RemoteColor = new Color(0.9f, 0.55f, 0.2f);

    [SerializeField]
    private Material? m_grayboxMaterial;

    [SerializeField]
    private InputActionAsset? m_inputActions;

    [SerializeField]
    private string m_host = "127.0.0.1";

    [SerializeField]
    private int m_port = 7777;

    [SerializeField]
    private string m_buildVersion = "0.2.0-dev";

    [SerializeField]
    private bool m_connectOnStart = true;

    private readonly Dictionary<EntityId, EntityView> m_remoteViews = new Dictionary<EntityId, EntityView>();
    private readonly StreamingContentLoader m_contentLoader = new StreamingContentLoader();
    private LiteNetLibClientTransport? m_socket;
    private ManualMoveSource? m_manualSource;
    private PointerMoveSource? m_pointerSource;
    private MovementController? m_controller;
    private LocalPlayerDriver? m_driver;
    private FixedTickClock? m_clock;
    private ClientWorld? m_world;
    private GrayboxMap? m_map;
    private EntityView? m_localView;
    private MoveMarker? m_marker;
    private FollowCamera? m_camera;
    private DevelopmentOverlay? m_overlay;
    private Material? m_runtimeMaterial;

    public string Host
    {
        get => m_host;
        set => m_host = value;
    }

    public int Port
    {
        get => m_port;
        set => m_port = value;
    }

    public string Identity { get; set; } = string.Empty;

    public long Character { get; set; }

    public string Status { get; private set; } = "Loading content";

    public ClientContent? Content => m_contentLoader.Content;

    public ClientConnection? Connection { get; private set; }

    public LossyTransport? Link { get; private set; }

    public ClientWorld? World => m_world;

    public MovementController? Controller => m_controller;

    public FixedTickClock? Clock => m_clock;

    public TouchControls? Touch { get; private set; }

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;

        // A second client on the same machine needs its own character, or the server replaces the first session.
        int suffix = Random.Range(1000, 9999);
        Identity = "player" + suffix;
        Character = suffix;

        Touch = TouchControls.Create();
        Touch.transform.SetParent(transform, false);
        Touch.SetVisible(Application.isMobilePlatform);
        m_overlay = gameObject.AddComponent<DevelopmentOverlay>();
        m_overlay.Bind(this);

        yield return m_contentLoader.Load();
        if (m_contentLoader.Content == null)
        {
            Status = m_contentLoader.Error;
            Debug.LogError(m_contentLoader.Error);
            yield break;
        }

        Status = "Content " + m_contentLoader.Content.Version;
        if (m_connectOnStart)
        {
            Connect();
        }
    }

    private void Update()
    {
        Connection?.Poll();
        if (m_world == null || m_driver == null || m_clock == null || m_controller == null)
        {
            return;
        }

        float yaw = m_camera == null ? 0f : m_camera.YawDegrees;
        m_manualSource?.Apply(m_controller, yaw);
        HandlePointerRequest();

        int ticks = m_clock.Advance(Time.unscaledDeltaTime);
        for (int index = 0; index < ticks; index++)
        {
            m_driver.Tick(m_clock.NextTick());
        }

        m_world.Advance(Time.unscaledDeltaTime);
    }

    private void LateUpdate()
    {
        if (m_world == null || m_clock == null)
        {
            return;
        }

        if (m_localView != null)
        {
            m_localView.SetPose(m_world.Smoother.Sample(m_clock.Alpha), m_world.Predictor.Facing);
        }

        foreach (KeyValuePair<EntityId, EntityView> pair in m_remoteViews)
        {
            double renderTime = m_world.RemoteRenderTime;
            if (m_world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote)
                && remote.Buffer.TrySample(renderTime, out WorldPosition position, out WorldDirection facing))
            {
                pair.Value.SetPose(position, facing);
            }
        }

        if (m_marker != null && m_controller != null)
        {
            m_marker.Refresh(m_controller.HasPath);
        }
    }

    private void OnDestroy()
    {
        TearDownWorld();
        m_pointerSource?.Dispose();
        m_socket?.Dispose();
        if (m_runtimeMaterial != null)
        {
            Destroy(m_runtimeMaterial);
        }
    }

    public void Connect()
    {
        bool isOpen = Connection != null && Connection.State != ClientConnectionState.Disconnected;
        if (m_contentLoader.Content == null || isOpen)
        {
            return;
        }

        if (Connection != null)
        {
            Connection.EnteredWorld -= OnEnteredWorld;
            Connection.Closed -= OnClosed;
        }

        m_socket?.Dispose();
        m_socket = new LiteNetLibClientTransport(ConnectionKey, DisconnectTimeoutMilliseconds);
        LossyTransport? previousLink = Link;
        Link = new LossyTransport(m_socket, System.Environment.TickCount, () => Time.realtimeSinceStartupAsDouble);
        if (previousLink != null)
        {
            Link.LatencyMilliseconds = previousLink.LatencyMilliseconds;
            Link.JitterMilliseconds = previousLink.JitterMilliseconds;
            Link.LossPercent = previousLink.LossPercent;
            Link.ReorderPercent = previousLink.ReorderPercent;
        }

        ClientConnectionSettings settings = new ClientConnectionSettings(
            m_buildVersion,
            m_contentLoader.Content.Version,
            DevelopmentTokenPrefix + Identity,
            new CharacterId(Character));
        Connection = new ClientConnection(Link, settings, m_contentLoader.Content);
        Connection.EnteredWorld += OnEnteredWorld;
        Connection.Closed += OnClosed;
        Status = "Connecting to " + m_host + ":" + m_port;
        Connection.Connect(m_host, m_port);
    }

    public void Disconnect()
    {
        Connection?.Disconnect();
    }

    private void OnEnteredWorld(ClientWorld world)
    {
        StartCoroutine(EnterMap(world));
    }

    private IEnumerator EnterMap(ClientWorld world)
    {
        if (m_contentLoader.Content == null
            || !m_contentLoader.Content.TryGetMap(world.Map, out ClientMap? map)
            || map == null
            || !MapSceneResolver.TryResolve(map.SceneKey, out string sceneName))
        {
            Status = "No scene is known for map " + world.Map.Value;
            Disconnect();
            yield break;
        }

        Status = "Loading " + map.DisplayName;
        yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (Connection == null || Connection.World != world)
        {
            yield break;
        }

        m_world = world;
        Material material = ResolveMaterial();
        m_map = GrayboxMap.Create(world.Grid, material);
        m_marker = MoveMarker.Create(material);
        m_localView = EntityView.Create("LocalPlayer", world.Grid.AgentRadius, material, LocalColor);
        foreach (RemoteEntity remote in world.Remotes.Values)
        {
            AddRemoteView(remote);
        }

        world.RemoteSpawned += AddRemoteView;
        world.RemoteDespawned += RemoveRemoteView;

        Camera? mainCamera = Camera.main;
        if (mainCamera != null)
        {
            m_camera = mainCamera.GetComponent<FollowCamera>();
            if (m_camera == null)
            {
                m_camera = mainCamera.gameObject.AddComponent<FollowCamera>();
            }

            m_camera.Follow(m_localView.transform);
        }

        BindInput();
        m_controller = new MovementController(world.Grid);
        m_clock = new FixedTickClock(1f / Connection.ServerTickRate);
        m_driver = new LocalPlayerDriver(m_controller, new MoveIntentProducer(), world, Connection);
        Status = "In " + map.DisplayName;
    }

    private void BindInput()
    {
        if (m_manualSource != null)
        {
            return;
        }

        InputActionAsset? actions = m_inputActions != null ? m_inputActions : InputSystem.actions;
        InputAction? move = actions?.FindAction("Player/Move");
        InputAction? moveTo = actions?.FindAction("Player/MoveTo");
        InputAction? pointerPosition = actions?.FindAction("Player/PointerPosition");
        if (move == null || moveTo == null || pointerPosition == null)
        {
            Status = "The input actions asset lacks Player/Move, Player/MoveTo, or Player/PointerPosition.";
            Debug.LogError(Status);
            return;
        }

        m_manualSource = new ManualMoveSource(move);
        m_pointerSource = new PointerMoveSource(moveTo, pointerPosition);
    }

    private void HandlePointerRequest()
    {
        if (m_pointerSource == null || !m_pointerSource.TryTakeRequest(out Vector2 screenPosition))
        {
            return;
        }

        Camera? mainCamera = Camera.main;
        bool isOnUi = (Touch != null && Touch.IsOverControl(screenPosition))
            || (m_overlay != null && m_overlay.Covers(screenPosition));
        if (isOnUi || mainCamera == null || m_map == null || m_map.GroundCollider == null
            || m_world == null || m_controller == null)
        {
            return;
        }

        if (!GroundPicker.TryPick(mainCamera, m_map.GroundCollider, screenPosition, out WorldPosition point))
        {
            return;
        }

        if (m_controller.TryMoveTo(m_world.Predictor.Position, point))
        {
            m_marker?.ShowAccepted(m_controller.Path[m_controller.Path.Count - 1]);
        }
        else
        {
            m_marker?.ShowRefused(point);
        }
    }

    private void OnClosed()
    {
        TearDownWorld();
        ClientConnection? connection = Connection;
        if (connection == null)
        {
            return;
        }

        if (connection.LocalError.Length > 0)
        {
            Status = connection.LocalError;
        }
        else if (connection.Notice != null)
        {
            Status = "Disconnected: " + connection.Notice.Reason + " " + connection.Notice.Message;
        }
        else
        {
            Status = "Disconnected: " + connection.DisconnectCause;
        }
    }

    private void TearDownWorld()
    {
        if (m_world != null)
        {
            m_world.RemoteSpawned -= AddRemoteView;
            m_world.RemoteDespawned -= RemoveRemoteView;
        }

        m_world = null;
        m_driver = null;
        m_controller = null;
        m_clock = null;
        foreach (EntityView view in m_remoteViews.Values)
        {
            DestroyView(view);
        }

        m_remoteViews.Clear();
        DestroyView(m_localView);
        DestroyView(m_marker);
        DestroyView(m_map);
        m_localView = null;
        m_marker = null;
        m_map = null;
        if (m_camera != null)
        {
            m_camera.Follow(null);
        }
    }

    private void AddRemoteView(RemoteEntity remote)
    {
        if (m_world == null)
        {
            return;
        }

        EntityView view = EntityView.Create(
            "Remote " + remote.Entity.Value,
            m_world.Grid.AgentRadius,
            ResolveMaterial(),
            RemoteColor);
        if (remote.Buffer.TrySample(double.MinValue, out WorldPosition position, out WorldDirection facing))
        {
            view.SetPose(position, facing);
        }

        m_remoteViews[remote.Entity] = view;
    }

    private void RemoveRemoteView(RemoteEntity remote)
    {
        if (m_remoteViews.TryGetValue(remote.Entity, out EntityView? view))
        {
            m_remoteViews.Remove(remote.Entity);
            DestroyView(view);
        }
    }

    private Material ResolveMaterial()
    {
        if (m_grayboxMaterial != null)
        {
            return m_grayboxMaterial;
        }

        if (m_runtimeMaterial == null)
        {
            m_runtimeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }

        return m_runtimeMaterial;
    }

    private static void DestroyView(MonoBehaviour? view)
    {
        if (view != null)
        {
            Destroy(view.gameObject);
        }
    }
}
}
