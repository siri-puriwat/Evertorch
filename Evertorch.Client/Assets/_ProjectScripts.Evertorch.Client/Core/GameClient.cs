using System;
using System.Collections;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
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

    private static readonly Color LocalColor = new(0.25f, 0.65f, 0.95f);
    private static readonly Color RemoteColor = new(0.9f, 0.55f, 0.2f);

    [SerializeField]
    private Material? m_grayboxMaterial;

    [SerializeField]
    private InputActionAsset? m_inputActions;

    [SerializeField]
    private string m_host = "127.0.0.1";

    // The gateway's port (Network Protocol §4); the game's own port comes with the sign-in's answer.
    [SerializeField]
    private int m_port = 7443;

    [SerializeField]
    private float m_cameraMaxYawDegrees = 90f;

    [SerializeField]
    private float m_cameraMinPitchDegrees = 30f;

    [SerializeField]
    private float m_cameraMaxPitchDegrees = 60f;

    [SerializeField]
    private float m_cameraMinDistance = 6f;

    [SerializeField]
    private float m_cameraMaxDistance = 20f;

    private readonly Dictionary<EntityId, EntityView> m_remoteViews = new();
    private readonly StreamingContentLoader m_contentLoader = new();
    private readonly EntityViewCatalog m_viewCatalog = new();
    private readonly List<PickCandidate> m_targetCandidates = new();
    private readonly List<PickCandidate> m_pointerCandidates = new();
    private readonly List<PickCandidate> m_npcCandidates = new();
    private readonly TargetCycler m_targetCycler = new();
    private readonly UiHitTest m_uiHitTest = new();
    private LiteNetLibClientTransport? m_socket;
    private ManualMoveSource? m_manualSource;
    private PointerMoveSource? m_pointerSource;
    private PointerMoveHandler? m_pointerHandler;
    private CombatInputSource? m_combatSource;
    private SkillInputSource? m_skillSource;
    private CameraInputSource? m_cameraSource;
    private TargetMarker? m_targetMarker;
    private AutoAttackState? m_autoAttack;
    private PickupState? m_pickup;
    private SkillState? m_skill;
    private TalkState? m_talk;
    private MovementController? m_controller;
    private LocalPlayerDriver? m_driver;
    private MoveIntentProducer? m_producer;
    private FixedTickClock? m_clock;
    private ClientWorld? m_world;
    private bool m_isChangingMap;
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
    private NpcWindow? m_npcWindow;
    private SkillBar? m_skillBar;
    private FeedbackLines? m_feedback;
    private CombatPresenter? m_combat;
    private ProjectilePresenter? m_projectiles;
    private Material? m_runtimeMaterial;
    private string m_leaveReason = string.Empty;
    private CharacterId m_lastCharacter;
    private CharacterId m_reconnectCharacter;
    private SignInAnswer? m_session;
    private string m_signedInHost = string.Empty;
    private int m_signedInPort;
    private bool m_isReusingSession;

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

    /// <summary>
    ///     The login and the password, held in memory only, so Reconnect can sign in again after the token expires.
    ///     Nothing stores or shows the password.
    /// </summary>
    public string Login { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    ///     The SHA-1 thumbprint of the one gateway certificate to trust, for live tests that must not depend on what the
    ///     machine trusts; null, the operating system decides. Never serialized.
    /// </summary>
    public string? PinnedThumbprint { get; set; }

    public bool IsSigningIn { get; private set; }

    public string Status { get; private set; } = "Loading content";

    public ClientContent? Content => m_contentLoader.Content;

    public ClientConnection? Connection { get; private set; }

    public LossyTransport? Link { get; private set; }

    public ClientWorld? World => m_world;

    /// <summary>
    ///     Whether the player is in the world: its map is drawn, or a map change is loading the next one. The panels for
    ///     a player out of the world stay hidden meanwhile.
    /// </summary>
    public bool IsInWorld => m_world != null || m_isChangingMap;

    public CombatPresenter? Combat => m_combat;

    public ProjectilePresenter? Projectiles => m_projectiles;

    public IReadOnlyDictionary<EntityId, EntityView> RemoteViews => m_remoteViews;

    public MovementController? Controller => m_controller;

    public FixedTickClock? Clock => m_clock;

    public TouchControls? Touch { get; private set; }

    /// <summary>
    ///     Where the camera sits around the player (Prototype Content §3). It lives here rather than on a map scene's
    ///     camera, so it survives the scene loads.
    /// </summary>
    public OrbitCameraState? CameraState { get; private set; }

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
        CameraState = new OrbitCameraState(
            m_cameraMaxYawDegrees,
            m_cameraMinPitchDegrees,
            m_cameraMaxPitchDegrees,
            m_cameraMinDistance,
            m_cameraMaxDistance);

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
        m_npcWindow = NpcWindow.Create(this);
        m_npcWindow.transform.SetParent(transform, false);
        m_skillBar = SkillBar.Create(this);
        m_skillBar.transform.SetParent(transform, false);
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

        // Nothing connects by itself: the player signs in from the login panel (Network Protocol §4).
        Status = $"Content {m_contentLoader.Content.Version}";
    }

    private void Update()
    {
        Connection?.Poll();
        if (m_world == null || m_driver == null || m_clock == null || m_controller == null)
        {
            // A click made while there is no world to walk in is dropped, not saved up for the next one.
            m_pointerSource?.TryTakeRequest(out Vector2 _);
            m_combatSource?.TakeRequest();
            m_skillSource?.TakeSlot();
            return;
        }

        if (CameraState != null)
        {
            m_cameraSource?.Apply(CameraState, Time.unscaledDeltaTime);
        }

        float yaw = m_camera == null ? 0f : m_camera.YawDegrees;
        m_manualSource?.Apply(m_controller, yaw);
        m_targetCandidates.Clear();
        m_world.CollectTargetCandidates(m_targetCandidates);
        m_pointerCandidates.Clear();
        m_pointerCandidates.AddRange(m_targetCandidates);
        m_world.CollectDropCandidates(m_pointerCandidates);
        m_npcCandidates.Clear();
        m_world.CollectNpcCandidates(m_npcCandidates);
        foreach (PickCandidate npc in m_npcCandidates)
        {
            m_pointerCandidates.Add(new PickCandidate(npc.Entity, OnPlinth(npc.Position)));
        }

        HandlePointerRequest();
        HandleCombatRequest();
        int slot = m_skillSource?.TakeSlot() ?? 0;
        if (slot != 0)
        {
            UseSkillSlot(slot);
        }

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
                pair.Value.SetPose(remote.Kind == EntityKind.Npc ? OnPlinth(position) : position, facing);
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
        m_projectiles?.Present(m_localView, m_remoteViews);
    }

    private void OnDestroy()
    {
        TearDownWorld();
        m_pointerSource?.Dispose();
        m_combatSource?.Dispose();
        m_skillSource?.Dispose();
        m_cameraSource?.Dispose();
        m_socket?.Dispose();
        m_viewCatalog.Dispose();
        if (m_runtimeMaterial != null)
        {
            Destroy(m_runtimeMaterial);
        }
    }

    /// <summary>
    ///     Signs in at the gateway with the login and the password, then connects with the token it gives.
    /// </summary>
    public void Connect()
    {
        m_reconnectCharacter = default;
        SignIn();
    }

    public void Disconnect()
    {
        Connection?.Disconnect();
    }

    /// <summary>
    ///     One press (Network Protocol §4): connects again with the kept token while the host and port are those of the
    ///     last sign-in, else signs in again first; if the kept token has expired or the server is no longer where it
    ///     was, signs in again once. Once the character list arrives, it enters the character last played. Only ever
    ///     on the player's request, never by itself.
    /// </summary>
    public void Reconnect()
    {
        if (!CanReconnect || !IsIdle())
        {
            return;
        }

        m_reconnectCharacter = m_lastCharacter;
        if (m_session != null
            && string.Equals(m_host, m_signedInHost, StringComparison.Ordinal)
            && m_port == m_signedInPort)
        {
            Open(m_session, true);
        }
        else
        {
            SignIn();
        }
    }

    private bool IsIdle()
    {
        bool isOpen = Connection != null && Connection.State != ClientConnectionState.Disconnected;
        return m_contentLoader.Content != null && !isOpen && !IsSigningIn;
    }

    private void SignIn()
    {
        if (!IsIdle())
        {
            return;
        }

        if (Login.Length == 0 || Password.Length == 0)
        {
            Status = SignInMessages.MissingCredentials;
            return;
        }

        IsSigningIn = true;
        CanReconnect = false;
        Status = SignInMessages.SigningIn;
        StartCoroutine(SignInThenOpen(new GatewaySignIn(m_host, m_port, Login, Password, PinnedThumbprint)));
    }

    private IEnumerator SignInThenOpen(GatewaySignIn signIn)
    {
        string host = m_host;
        int port = m_port;
        yield return signIn.Run();
        IsSigningIn = false;
        if (signIn.Answer == null)
        {
            Status = signIn.Error;
            yield break;
        }

        m_session = signIn.Answer;
        m_signedInHost = host;
        m_signedInPort = port;
        Open(m_session, false);
    }

    private void Open(SignInAnswer session, bool isReusingSession)
    {
        if (!IsIdle())
        {
            return;
        }

        m_isReusingSession = isReusingSession;
        if (Connection != null)
        {
            Connection.CharactersChanged -= OnCharactersChanged;
            Connection.EnteredWorld -= OnEnteredWorld;
            Connection.ChangedMap -= OnChangedMap;
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
            m_contentLoader.Content!.Version,
            session.Token);
        Connection = new ClientConnection(Link, settings, m_contentLoader.Content);
        Connection.CharactersChanged += OnCharactersChanged;
        Connection.EnteredWorld += OnEnteredWorld;
        Connection.ChangedMap += OnChangedMap;
        Connection.LeftWorld += OnLeftWorld;
        Connection.Closed += OnClosed;
        Status = $"Connecting to {session.Host}:{session.Port}";
        Connection.Connect(session.Host, session.Port);
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
    ///     A press of an inventory row (Prototype Content §2): what the row's item asks for, as the server last
    ///     committed the row.
    /// </summary>
    public void PressInventoryRow(InventoryEntry row)
    {
        ClientContent? content = m_contentLoader.Content;
        if (Connection != null
            && content != null
            && content.TryGetItem(row.Item, out ClientItem? item)
            && item != null)
        {
            InventoryActions.Press(Connection, row, item.Type);
        }
    }

    /// <summary>
    ///     Asks the NPC for <paramref name="quantity" /> of <paramref name="item" /> (Gameplay Systems §11.3). The server
    ///     checks the catalogue, the coins, and the room, and only its committed change shows the purchase.
    /// </summary>
    public void BuyFrom(EntityId npc, ItemDefinitionId item, uint quantity)
    {
        Connection?.SendBuy(npc, item, quantity);
    }

    /// <summary>
    ///     Offers the NPC <paramref name="quantity" /> of an inventory row (Gameplay Systems §11.3). The server checks
    ///     the row, its price, and the coin cap, and only its committed change shows the sale.
    /// </summary>
    public void SellTo(EntityId npc, long inventoryItem, uint quantity)
    {
        Connection?.SendSell(npc, inventoryItem, quantity);
    }

    /// <summary>
    ///     Asks the NPC for <paramref name="quest" /> (Gameplay Systems §2.2); the quest log shows it once the server
    ///     accepts.
    /// </summary>
    public void AcceptQuestFrom(EntityId npc, QuestDefinitionId quest)
    {
        Connection?.SendAcceptQuest(npc, quest);
    }

    /// <summary>
    ///     Turns <paramref name="quest" /> in to the NPC (Gameplay Systems §2.2); the reward shows only once the server's
    ///     commit returns.
    /// </summary>
    public void TurnInQuestTo(EntityId npc, QuestDefinitionId quest)
    {
        Connection?.SendCompleteQuest(npc, quest);
    }

    /// <summary>
    ///     Uses the skill in a slot of the skill bar, numbered from 1 (Prototype Content §4): what the slot's key,
    ///     gamepad button, and button on the bar ask for. An enemy skill is for the confirmed target.
    /// </summary>
    public void UseSkillSlot(int slot)
    {
        if (SkillSlots.TryGetItem(slot, out ItemDefinitionId item))
        {
            if (m_world != null
                && Connection != null
                && InventoryActions.TryFindRow(m_world.Inventory.Rows, item, out InventoryEntry row))
            {
                Connection.SendUseItem(row.InventoryItem);
            }

            return;
        }

        ClientContent? content = m_contentLoader.Content;
        if (m_world == null
            || m_skill == null
            || content == null
            || !SkillSlots.TryGetSkill(slot, out SkillDefinitionId skill)
            || !content.TryGetSkill(skill, out ClientSkill? definition)
            || definition == null)
        {
            return;
        }

        if (definition.TargetType == SkillTargetType.Enemy && m_world.Target == default)
        {
            m_feedback?.Add("Choose a target first.");
            return;
        }

        m_pickup?.Cancel();
        m_talk?.Cancel();
        m_skill.Use(skill, definition.TargetType);
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
        LeaveWorld();
        Status = "Logged out: choose or create a character";
    }

    private void OnEnteredWorld(ClientWorld world)
    {
        // The server names the character it entered: after two quick requests it need not be the last one asked for.
        m_lastCharacter = world.Character;
        StartCoroutine(EnterMap(world));
    }

    // The old world stops at once (Gameplay Systems §5.1). The movement sequence and the client tick go on: the
    // server keeps its input queue across the change and would take a sequence started again for stale input.
    private void OnChangedMap(ClientWorld world)
    {
        TearDownWorld();
        m_isChangingMap = true;
        m_lastCharacter = world.Character;
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

        // The connection may have closed or been replaced, or the character moved on to another map, while the scene
        // was loading. Only the newest world is drawn, and a map change never falls back to the menu.
        if (Connection == null || Connection.State != ClientConnectionState.InWorld || Connection.World != world)
        {
            if (!IsInWorld)
            {
                LeaveMapScene();
            }

            yield break;
        }

        m_isChangingMap = false;
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
        m_projectiles = new ProjectilePresenter(
            world,
            m_contentLoader.Content,
            m_viewCatalog,
            1.0 / Connection.ServerTickRate,
            material);

        Camera? mainCamera = Camera.main;
        if (mainCamera != null)
        {
            m_camera = mainCamera.GetComponent<FollowCamera>();
            if (m_camera == null)
            {
                m_camera = mainCamera.gameObject.AddComponent<FollowCamera>();
            }

            m_camera.Follow(m_localView.transform, CameraState, m_map.GroundCollider);
        }

        BindInput();
        m_controller = new MovementController(world.Grid);
        m_clock ??= new FixedTickClock(1f / Connection.ServerTickRate);
        m_producer ??= new MoveIntentProducer();
        m_autoAttack = new AutoAttackState(world, m_controller, Connection, 1.0 / Connection.ServerTickRate);
        m_pickup = new PickupState(world, m_controller, Connection);
        m_skill = new SkillState(world, m_controller, Connection, 1.0 / Connection.ServerTickRate);
        m_talk = new TalkState(world, m_controller);
        m_talk.Arrived += OnTalkArrived;
        m_driver = new LocalPlayerDriver(
            m_controller,
            m_producer,
            world,
            Connection,
            m_autoAttack,
            m_pickup,
            m_skill,
            m_talk);
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

        InputAction? look = actions?.FindAction("Player/Look");
        InputAction? orbit = actions?.FindAction("Player/Orbit");
        InputAction? zoom = actions?.FindAction("Player/Zoom");
        InputAction? zoomStep = actions?.FindAction("Player/ZoomStep");
        Func<TouchControl, bool>? isGestureTap = null;
        if (look != null && orbit != null && zoom != null && zoomStep != null)
        {
            m_cameraSource = new CameraInputSource(look, orbit, zoom, zoomStep, m_uiHitTest);
            isGestureTap = m_cameraSource.IsGestureTap;
        }

        m_manualSource = new ManualMoveSource(move);
        m_pointerSource = new PointerMoveSource(moveTo, isGestureTap);
        m_pointerHandler = new PointerMoveHandler(m_pointerSource, m_uiHitTest);

        InputAction? next = actions?.FindAction("Player/Next");
        InputAction? previous = actions?.FindAction("Player/Previous");
        InputAction? clear = actions?.FindAction("Player/ClearTarget");
        InputAction? attack = actions?.FindAction("Player/Attack");
        InputAction? respawn = actions?.FindAction("Player/Respawn");
        InputAction? pickup = actions?.FindAction("Player/Pickup");
        InputAction? talk = actions?.FindAction("Player/Talk");
        if (next != null
            && previous != null
            && clear != null
            && attack != null
            && respawn != null
            && pickup != null
            && talk != null)
        {
            m_combatSource = new CombatInputSource(next, previous, clear, attack, respawn, pickup, talk);
        }

        var slots = new List<InputAction>();
        for (int slot = 1; slot <= SkillSlots.Count; slot++)
        {
            InputAction? action = actions?.FindAction($"Player/Slot{slot}");
            if (action != null)
            {
                slots.Add(action);
            }
        }

        if (slots.Count == SkillSlots.Count)
        {
            m_skillSource = new SkillInputSource(slots);
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
            m_skill?.Cancel();
            m_talk?.Cancel();
            m_marker?.ShowAccepted(m_controller.Path[m_controller.Path.Count - 1]);
        }
        else if (result == PointerMoveResult.Refused)
        {
            m_marker?.ShowRefused(point);
        }
        else if (result == PointerMoveResult.Entity)
        {
            // A click or tap on a monster attacks it, one on a drop picks it up, as in the reference game, and one on
            // an NPC talks to it, never attacks it (Prototype Content §4).
            EntityKind kind = m_world.Remotes.TryGetValue(entity, out RemoteEntity? remote)
                ? remote.Kind
                : EntityKind.None;
            if (kind == EntityKind.ItemDrop)
            {
                StartPickup(entity);
            }
            else if (kind == EntityKind.Npc)
            {
                StartTalk(entity);
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
        else if (request == CombatRequest.Talk)
        {
            EntityId npc = m_world.NearestNpc(m_world.Predictor.Position);
            if (npc != default)
            {
                StartTalk(npc);
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
        m_skill?.Cancel();
        m_talk?.Cancel();
        m_autoAttack?.Attack(target);
    }

    // A pickup replaces an auto-attack, whose chase would otherwise pull the character away from the drop.
    private void StartPickup(EntityId drop)
    {
        m_autoAttack?.OnWalkRequested();
        m_skill?.Cancel();
        m_talk?.Cancel();
        m_pickup?.Pickup(drop);
    }

    // Talking replaces whatever else the character was doing; it only walks.
    private void StartTalk(EntityId npc)
    {
        m_autoAttack?.OnWalkRequested();
        m_pickup?.Cancel();
        m_skill?.Cancel();
        m_talk?.Talk(npc);
    }

    private void OnTalkArrived(EntityId npc)
    {
        m_npcWindow?.Open(npc);
    }

    // An NPC stands on the plinth drawn over its marker cell (Prototype Content §5).
    private static WorldPosition OnPlinth(WorldPosition position)
    {
        return new WorldPosition(position.X, position.Y + GrayboxMeshBuilder.NpcMarkerHeight, position.Z);
    }

    private void OnClosed()
    {
        LeaveWorld();
        ClientConnection? connection = Connection;
        if (connection != null && IsStaleSession(connection))
        {
            // Still the one press of Reconnect: its kept token expired, or the server moved, so sign in again once.
            m_isReusingSession = false;
            m_session = null;
            SignIn();
            return;
        }

        m_reconnectCharacter = default;
        m_isReusingSession = false;
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

    private bool IsStaleSession(ClientConnection connection)
    {
        return m_isReusingSession
            && (connection.Notice?.Reason == DisconnectReason.SessionExpired
                || (connection.Notice == null &&
                    connection.DisconnectCause == TransportDisconnectCause.ConnectionFailed));
    }

    // Only a new entry starts the movement sequence and the client tick again, as the server starts its input state.
    private void LeaveWorld()
    {
        TearDownWorld();
        m_isChangingMap = false;
        m_producer = null;
        m_clock = null;
        LeaveMapScene();
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
        m_projectiles?.Dispose();
        m_projectiles = null;
        m_world = null;
        m_driver = null;
        m_autoAttack = null;
        m_pickup = null;
        m_skill = null;
        if (m_talk != null)
        {
            m_talk.Arrived -= OnTalkArrived;
        }

        m_talk = null;

        // A Unity comparison: when the client itself is destroyed, the window may already be gone.
        if (m_npcWindow != null)
        {
            m_npcWindow.Close();
        }

        m_controller = null;
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
            m_camera.Follow(null, null, null);
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
