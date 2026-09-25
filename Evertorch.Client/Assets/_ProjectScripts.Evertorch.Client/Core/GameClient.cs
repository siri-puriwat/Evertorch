using System;
using System.Collections;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client
{
/// <summary>
///     The client's composition root. It loads content, owns the connection, runs the client simulation at the server's
///     tick rate, and keeps the views in step with it. All state lives on this instance; nothing is static, because the
///     editor may keep the domain loaded between play sessions.
/// </summary>
public sealed class GameClient : MonoBehaviour
{
    private const string ConnectionKey = "evertorch";
    private const int DisconnectTimeoutMilliseconds = 10000;
    private const string DevelopmentTokenPrefix = "dev:";

    private static readonly Color LocalColor = new(0.25f, 0.65f, 0.95f);
    private static readonly Color RemoteColor = new(0.9f, 0.55f, 0.2f);

    [SerializeField]
    private Material? m_grayboxMaterial;

    [SerializeField]
    private InputActionAsset? m_inputActions;

    [SerializeField]
    private string m_host = "127.0.0.1";

    [SerializeField]
    private int m_port = 7777;

    [SerializeField]
    private bool m_connectOnStart = true;

    private readonly Dictionary<EntityId, EntityView> m_remoteViews = new();
    private readonly StreamingContentLoader m_contentLoader = new();
    private readonly EntityViewCatalog m_viewCatalog = new();
    private readonly List<PickCandidate> m_targetCandidates = new();
    private readonly List<PickCandidate> m_pointerCandidates = new();
    private readonly TargetCycler m_targetCycler = new();
    private readonly UiHitTest m_uiHitTest = new();
    private LiteNetLibClientTransport? m_socket;
    private ManualMoveSource? m_manualSource;
    private PointerMoveSource? m_pointerSource;
    private PointerMoveHandler? m_pointerHandler;
    private CombatInputSource? m_combatSource;
    private TargetMarker? m_targetMarker;
    private AutoAttackState? m_autoAttack;
    private PickupState? m_pickup;
    private MovementController? m_controller;
    private LocalPlayerDriver? m_driver;
    private FixedTickClock? m_clock;
    private ClientWorld? m_world;
    private GrayboxMap? m_map;
    private EntityView? m_localView;
    private MoveMarker? m_marker;
    private FollowCamera? m_camera;
    private DevelopmentOverlay? m_overlay;
    private CombatHud? m_hud;
    private LoginPanel? m_login;
    private StatusBar? m_statusBar;
    private TargetFrame? m_targetFrame;
    private InventoryWindow? m_inventoryWindow;
    private FeedbackLines? m_feedback;
    private CombatPresenter? m_combat;
    private Material? m_runtimeMaterial;
    private string m_leaveReason = string.Empty;
    private CharacterId m_enteringCharacter;
    private CharacterId m_lastCharacter;
    private CharacterId m_reconnectCharacter;

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

    public string Status { get; private set; } = "Loading content";

    public ClientContent? Content => m_contentLoader.Content;

    public ClientConnection? Connection { get; private set; }

    public LossyTransport? Link { get; private set; }

    public ClientWorld? World => m_world;

    public CombatPresenter? Combat => m_combat;

    public IReadOnlyDictionary<EntityId, EntityView> RemoteViews => m_remoteViews;

    public MovementController? Controller => m_controller;

    public FixedTickClock? Clock => m_clock;

    public TouchControls? Touch { get; private set; }

    /// <summary>
    ///     Whether the last close allows <see cref="Reconnect" />: not after a refusal only an update or another sign-in
    ///     can cure (<see cref="DisconnectMessages.CanReconnect" />).
    /// </summary>
    public bool CanReconnect { get; private set; }

    /// <summary>
    ///     The list entry of the character in the world, for its name and level, which the world messages do not carry;
    ///     null outside the world.
    /// </summary>
    public CharacterListEntry? PlayedCharacter
    {
        get
        {
            IReadOnlyList<CharacterListEntry>? characters = m_world != null ? Connection?.Characters : null;
            if (characters == null)
            {
                return null;
            }

            for (int index = 0; index < characters.Count; index++)
            {
                if (characters[index].Character == m_lastCharacter)
                {
                    return characters[index];
                }
            }

            return null;
        }
    }

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;

        // Each identity is its own account. Every client on the machine, including each Multiplayer Play Mode
        // window, starts with its own, so windows started together never share characters. A GUID is random per
        // process, unlike a seeded draw two editors started together could share. Type an identity in the login
        // panel to come back to the same account.
        int suffix = 100000 + Math.Abs(Guid.NewGuid().GetHashCode() % 900000);
        Identity = $"player{suffix}";

        m_overlay = DevelopmentOverlay.Create(this);
        m_overlay.transform.SetParent(transform, false);
        // The Dev button is for a device without a keyboard; everywhere else F1 shows the overlay.
        UnityAction? toggleOverlay = Application.isMobilePlatform ? m_overlay.Toggle : null;
        Touch = TouchControls.Create(toggleOverlay);
        Touch.transform.SetParent(transform, false);
        Touch.SetVisible(Application.isMobilePlatform);
        m_hud = CombatHud.Create(this);
        m_hud.transform.SetParent(transform, false);
        m_statusBar = StatusBar.Create(this);
        m_statusBar.transform.SetParent(transform, false);
        m_targetFrame = TargetFrame.Create(this);
        m_targetFrame.transform.SetParent(transform, false);
        m_inventoryWindow = InventoryWindow.Create(this);
        m_inventoryWindow.transform.SetParent(transform, false);
        m_feedback = FeedbackLines.Create(this);
        m_feedback.transform.SetParent(transform, false);
        m_login = LoginPanel.Create(this);
        m_login.transform.SetParent(transform, false);

        yield return m_contentLoader.Load();
        if (m_contentLoader.Content == null)
        {
            Status = m_contentLoader.Error;
            Debug.LogError(m_contentLoader.Error);
            yield break;
        }

        Status = $"Content {m_contentLoader.Content.Version}";
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
            // A click made while there is no world to walk in is dropped, not saved up for the next one.
            m_pointerSource?.TryTakeRequest(out Vector2 _);
            m_combatSource?.TakeRequest();
            return;
        }

        float yaw = m_camera == null ? 0f : m_camera.YawDegrees;
        m_manualSource?.Apply(m_controller, yaw);
        m_targetCandidates.Clear();
        m_world.CollectTargetCandidates(m_targetCandidates);
        m_pointerCandidates.Clear();
        m_pointerCandidates.AddRange(m_targetCandidates);
        m_world.CollectDropCandidates(m_pointerCandidates);
        HandlePointerRequest();
        HandleCombatRequest();

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

        if (m_targetMarker != null)
        {
            if (m_remoteViews.TryGetValue(m_world.Target, out EntityView? target))
            {
                Vector3 at = target.transform.position;
                m_targetMarker.Show(new WorldPosition(at.x, at.y, at.z));
            }
            else
            {
                m_targetMarker.Hide();
            }
        }

        m_combat?.Present(m_localView, m_remoteViews, Camera.main);
    }

    private void OnDestroy()
    {
        TearDownWorld();
        m_pointerSource?.Dispose();
        m_combatSource?.Dispose();
        m_socket?.Dispose();
        m_viewCatalog.Dispose();
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
            Connection.CharactersChanged -= OnCharactersChanged;
            Connection.EnteredWorld -= OnEnteredWorld;
            Connection.LeftWorld -= OnLeftWorld;
            Connection.Closed -= OnClosed;
        }

        m_socket?.Dispose();
        m_socket = new LiteNetLibClientTransport(ConnectionKey, DisconnectTimeoutMilliseconds);
        LossyTransport? previousLink = Link;
        Link = new LossyTransport(m_socket, Environment.TickCount, () => Time.realtimeSinceStartupAsDouble);
        if (previousLink != null)
        {
            Link.LatencyMilliseconds = previousLink.LatencyMilliseconds;
            Link.JitterMilliseconds = previousLink.JitterMilliseconds;
            Link.LossPercent = previousLink.LossPercent;
            Link.ReorderPercent = previousLink.ReorderPercent;
        }

        var settings = new ClientConnectionSettings(
            ProtocolConstants.BuildVersion,
            m_contentLoader.Content.Version,
            DevelopmentTokenPrefix + Identity);
        Connection = new ClientConnection(Link, settings, m_contentLoader.Content);
        Connection.CharactersChanged += OnCharactersChanged;
        Connection.EnteredWorld += OnEnteredWorld;
        Connection.LeftWorld += OnLeftWorld;
        Connection.Closed += OnClosed;
        Status = $"Connecting to {m_host}:{m_port}";
        Connection.Connect(m_host, m_port);
    }

    public void Disconnect()
    {
        Connection?.Disconnect();
    }

    /// <summary>
    ///     Connects again and, once the character list arrives, enters the character last played. Only ever on the
    ///     player's request, never by itself.
    /// </summary>
    public void Reconnect()
    {
        if (!CanReconnect)
        {
            return;
        }

        m_reconnectCharacter = m_lastCharacter;
        Connect();
    }

    /// <summary>
    ///     Asks for a new character on this identity's account; the answer comes back with the next character list.
    /// </summary>
    public void CreateCharacter(string name)
    {
        if (Connection != null && Connection.CreateCharacter(name))
        {
            Status = $"Creating {name}";
        }
    }

    /// <summary>
    ///     Leaves the world for character selection once the server has saved the character.
    /// </summary>
    public void Logout()
    {
        if (Connection != null && Connection.State == ClientConnectionState.InWorld)
        {
            Connection.SendLogout();
            Status = "Logging out";
        }
    }

    public void EnterWorld(CharacterId character)
    {
        if (Connection != null && Connection.EnterWorld(character))
        {
            m_enteringCharacter = character;
            Status = "Entering the world";
        }
    }

    /// <summary>
    ///     Asks the server to bring the dead local player back at the map's spawn point.
    /// </summary>
    public void RequestRespawn()
    {
        if (m_world != null && m_world.IsLocalDead)
        {
            Connection?.SendRespawn();
        }
    }

    /// <summary>
    ///     The status line after a character list, or null to keep the current one. While an entry is unanswered the
    ///     player can still create a character, and the list that follows says why a creation was refused; a list that
    ///     only crossed the entry request says nothing new.
    /// </summary>
    public static string? CharacterListStatus(ClientConnectionState state, CreateCharacterOutcome outcome)
    {
        bool isAnswer = state == ClientConnectionState.SelectingCharacter
            || (state == ClientConnectionState.EnteringWorld && outcome != CreateCharacterOutcome.None);
        if (!isAnswer)
        {
            return null;
        }

        return outcome switch
        {
            CreateCharacterOutcome.None => "Choose or create a character",
            CreateCharacterOutcome.Created => "Character created",
            CreateCharacterOutcome.NameInvalid => "Name refused: 4 to 23 letters and digits",
            CreateCharacterOutcome.NameTaken => "Name refused: already taken",
            CreateCharacterOutcome.LimitReached => "Refused: an account holds at most 3 characters",
            _ => "The server cannot create characters right now; try again"
        };
    }

    private void OnCharactersChanged()
    {
        ClientConnection? connection = Connection;
        string? status = connection == null
            ? null
            : CharacterListStatus(connection.State, connection.LastCreateOutcome);
        if (status != null)
        {
            Status = status;
        }

        EnterAgainAfterReconnect();
    }

    private void EnterAgainAfterReconnect()
    {
        CharacterId character = m_reconnectCharacter;
        m_reconnectCharacter = default;
        if (character == default || Connection?.State != ClientConnectionState.SelectingCharacter)
        {
            return;
        }

        foreach (CharacterListEntry entry in Connection.Characters)
        {
            if (entry.Character == character)
            {
                EnterWorld(character);
                return;
            }
        }
    }

    private void OnLeftWorld()
    {
        TearDownWorld();
        LeaveMapScene();
        Status = "Logged out: choose or create a character";
    }

    private void OnEnteredWorld(ClientWorld world)
    {
        m_lastCharacter = m_enteringCharacter;
        StartCoroutine(EnterMap(world));
    }

    private IEnumerator EnterMap(ClientWorld world)
    {
        if (m_contentLoader.Content == null
            || !m_contentLoader.Content.TryGetMap(world.Map, out ClientMap? map)
            || map == null
            || !MapSceneResolver.TryResolve(map.SceneKey, out string sceneName))
        {
            m_leaveReason = $"No scene is known for map {world.Map.Value}";
            Disconnect();
            yield break;
        }

        Status = $"Loading {map.DisplayName}";
        yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

        // The connection may have closed, or been replaced, while the scene was loading.
        if (Connection == null || Connection.State != ClientConnectionState.InWorld || Connection.World != world)
        {
            if (m_world == null)
            {
                LeaveMapScene();
            }

            yield break;
        }

        m_world = world;
        Material material = ResolveMaterial();
        m_map = GrayboxMap.Create(world.Grid, material);
        m_marker = MoveMarker.Create(material);
        m_targetMarker = TargetMarker.Create(material);
        m_localView = EntityView.Create(
            "LocalPlayer",
            EntityViewKeys.ForJob(m_contentLoader.Content, world.LocalJob),
            m_viewCatalog,
            LocalColor);
        foreach (RemoteEntity remote in world.Remotes.Values)
        {
            AddRemoteView(remote);
        }

        world.RemoteSpawned += AddRemoteView;
        world.RemoteDespawned += RemoveRemoteView;
        m_combat = new CombatPresenter(world, 1.0 / Connection.ServerTickRate, material);

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
        m_autoAttack = new AutoAttackState(world, m_controller, Connection, 1.0 / Connection.ServerTickRate);
        m_pickup = new PickupState(world, m_controller, Connection);
        m_driver = new LocalPlayerDriver(
            m_controller,
            new MoveIntentProducer(),
            world,
            Connection,
            m_autoAttack,
            m_pickup);
        Status = $"In {map.DisplayName}";
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
        if (move == null || moveTo == null)
        {
            Status = "The input actions asset lacks Player/Move or Player/MoveTo.";
            Debug.LogError(Status);
            return;
        }

        m_manualSource = new ManualMoveSource(move);
        m_pointerSource = new PointerMoveSource(moveTo);
        m_pointerHandler = new PointerMoveHandler(m_pointerSource, m_uiHitTest);

        InputAction? next = actions?.FindAction("Player/Next");
        InputAction? previous = actions?.FindAction("Player/Previous");
        InputAction? clear = actions?.FindAction("Player/ClearTarget");
        InputAction? attack = actions?.FindAction("Player/Attack");
        InputAction? respawn = actions?.FindAction("Player/Respawn");
        InputAction? pickup = actions?.FindAction("Player/Pickup");
        if (next != null && previous != null && clear != null && attack != null && respawn != null && pickup != null)
        {
            m_combatSource = new CombatInputSource(next, previous, clear, attack, respawn, pickup);
        }
    }

    private void HandlePointerRequest()
    {
        if (m_pointerHandler == null || m_world == null || m_controller == null)
        {
            return;
        }

        Collider? ground = m_map == null ? null : m_map.GroundCollider;
        PointerMoveResult result = m_pointerHandler.Handle(
            Camera.main,
            ground,
            m_pointerCandidates,
            m_controller,
            m_world.Predictor.Position,
            out WorldPosition point,
            out EntityId entity);
        if (result == PointerMoveResult.Accepted)
        {
            m_autoAttack?.OnWalkRequested();
            m_pickup?.Cancel();
            m_marker?.ShowAccepted(m_controller.Path[m_controller.Path.Count - 1]);
        }
        else if (result == PointerMoveResult.Refused)
        {
            m_marker?.ShowRefused(point);
        }
        else if (result == PointerMoveResult.Entity)
        {
            // A click or tap on a monster attacks it and one on a drop picks it up, as in the reference game
            // (Prototype Content §4).
            if (m_world.Remotes.TryGetValue(entity, out RemoteEntity? remote) && remote.Kind == EntityKind.ItemDrop)
            {
                StartPickup(entity);
            }
            else
            {
                Attack(entity);
            }
        }
    }

    private void HandleCombatRequest()
    {
        if (m_combatSource == null || m_world == null)
        {
            return;
        }

        CombatRequest request = m_combatSource.TakeRequest();
        if (request == CombatRequest.Clear)
        {
            Connection?.SendTarget(default);
        }
        else if (request == CombatRequest.Attack)
        {
            Attack(m_world.Target);
        }
        else if (request == CombatRequest.Pickup)
        {
            EntityId drop = m_world.NearestDrop(m_world.Predictor.Position, PickupState.KeyReach);
            if (drop != default)
            {
                StartPickup(drop);
            }
        }
        else if (request == CombatRequest.Respawn)
        {
            RequestRespawn();
        }
        else if (request != CombatRequest.None)
        {
            EntityId next = m_targetCycler.Choose(
                m_targetCandidates,
                m_world.Predictor.Position,
                m_world.Target,
                request == CombatRequest.Next);
            if (next != default)
            {
                Connection?.SendTarget(next);
            }
        }
    }

    private void Attack(EntityId target)
    {
        m_pickup?.Cancel();
        m_autoAttack?.Attack(target);
    }

    // A pickup replaces an auto-attack, whose chase would otherwise pull the character away from the drop.
    private void StartPickup(EntityId drop)
    {
        m_autoAttack?.OnWalkRequested();
        m_pickup?.Pickup(drop);
    }

    private void OnClosed()
    {
        TearDownWorld();
        LeaveMapScene();
        m_reconnectCharacter = default;
        ClientConnection? connection = Connection;
        if (connection == null)
        {
            return;
        }

        if (m_leaveReason.Length > 0)
        {
            Status = m_leaveReason;
            m_leaveReason = string.Empty;
            CanReconnect = false;
        }
        else if (connection.LocalError.Length > 0)
        {
            Status = connection.LocalError;
            CanReconnect = false;
        }
        else
        {
            Status = DisconnectMessages.Describe(connection.Notice, connection.DisconnectCause);
            CanReconnect = DisconnectMessages.CanReconnect(connection.Notice);
        }
    }

    // Out of the world the map scene would be an empty stage, so the client shows the main menu scene instead.
    private static void LeaveMapScene()
    {
        if (MapSceneResolver.IsMapScene(SceneManager.GetActiveScene().name))
        {
            SceneManager.LoadScene(BootstrapRedirect.MainMenuScene);
        }
    }

    private void TearDownWorld()
    {
        if (m_world != null)
        {
            m_world.RemoteSpawned -= AddRemoteView;
            m_world.RemoteDespawned -= RemoveRemoteView;
        }

        m_combat?.Dispose();
        m_combat = null;
        m_world = null;
        m_driver = null;
        m_autoAttack = null;
        m_pickup = null;
        m_controller = null;
        m_clock = null;
        foreach (EntityView view in m_remoteViews.Values)
        {
            DestroyView(view);
        }

        m_remoteViews.Clear();
        DestroyView(m_localView);
        DestroyView(m_marker);
        DestroyView(m_targetMarker);
        DestroyView(m_map);
        m_localView = null;
        m_marker = null;
        m_targetMarker = null;
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

        var view = EntityView.Create(
            $"Remote {remote.Entity.Value}",
            EntityViewKeys.ForEntity(m_contentLoader.Content, remote.Kind, remote.DefinitionId),
            m_viewCatalog,
            remote.Kind == EntityKind.Player ? RemoteColor : null);
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
