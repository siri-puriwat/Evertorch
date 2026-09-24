using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The real session, world, and visibility code wired to an in-memory transport and ticked by hand. Nothing here
///     stands in for server logic; only the socket and the clock are replaced.
/// </summary>
internal sealed class TestServer
{
    public const string BuildVersion = ProtocolConstants.BuildVersion;
    public const string DevelopmentToken = "dev:tester";
    public const int TickRate = 20;

    private static readonly Lazy<ServerContent> RepositoryContent =
        new(() => ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage()));

    // Most server tests are about players; without monsters their message counts stay exact.
    private static readonly Lazy<ServerContent> RepositoryContentWithoutMonsters =
        new(() => WithoutMonsterSpawns(RepositoryContent.Value));

    private readonly TickPipeline m_pipeline;
    private readonly List<Action> m_afterCommands = new();
    private long m_lastConnection;
    private uint m_tick;

    public TestServer(
        bool isDevelopmentAuthenticationEnabled = true,
        int handshakeTimeoutMs = 5000,
        int maxInboundEvents = 4096,
        float interestCellSize = 16f,
        int interestNeighborRadius = 1,
        int inputHoldTimeoutMs = 250,
        int maxQueuedInputs = 3,
        int snapshotIntervalTicks = 1,
        bool withMonsters = false,
        ulong randomSeed = 1,
        IRandomSource? combatRandom = null,
        bool withMonsterAi = true,
        IRandomSource? dropRandom = null,
        int itemDropLifetimeMs = 60000,
        PersistenceOptions? persistence = null,
        IGameStore? store = null,
        int reconnectGraceMs = 0,
        bool isAbuseControlEnabled = true)
    {
        Content = withMonsters ? RepositoryContent.Value : RepositoryContentWithoutMonsters.Value;
        var network = new NetworkOptions
        {
            HandshakeTimeoutMs = handshakeTimeoutMs,
            MaxInboundEvents = maxInboundEvents
        };
        var world = new WorldOptions
        {
            InterestCellSize = interestCellSize,
            InterestNeighborRadius = interestNeighborRadius,
            InputHoldTimeoutMs = inputHoldTimeoutMs,
            MaxQueuedInputs = maxQueuedInputs,
            SnapshotIntervalTicks = snapshotIntervalTicks,
            RandomSeed = randomSeed,
            ItemDropLifetimeMs = itemDropLifetimeMs
        };
        var authentication = new DevelopmentAuthenticationOptions
        {
            Enabled = isDevelopmentAuthenticationEnabled
        };
        IOptions<CompatibilityOptions> compatibility = Options.Create(new CompatibilityOptions());
        IOptions<SimulationOptions> simulation = Options.Create(new SimulationOptions { TickRate = TickRate });

        Random = ServerRandom.FromOptions(world);
        Transport = new InMemoryServerTransport();
        IOptions<AbuseOptions> abuse = Options.Create(new AbuseOptions { Enabled = isAbuseControlEnabled });
        Inbound = new InboundQueue(Options.Create(network), abuse, simulation, Clock, Instruments);
        Sessions = new SessionRegistry();
        World = new WorldSimulation(
            Content,
            Options.Create(world),
            new RenewalCharacterRules(),
            new RenewalMovementRules(),
            Random);
        Log = new CapturingLogger<SessionManager>();
        Time = new FakeTimeProvider();
        GameStore = store ?? new InMemoryGameStore();
        PersistenceLog = new CapturingLogger<PersistenceWorker>();
        Persistence = new PersistenceWorker(
            GameStore,
            Options.Create(persistence ?? new PersistenceOptions { RetryBaseDelayMs = 1 }),
            Instruments,
            PersistenceLog);

        var sender = new MessageSender(Transport);
        var targeting = new Targeting(sender);
        Lifetime = new CharacterLifetime(
            World,
            Sessions,
            Persistence,
            Time,
            Options.Create(persistence ?? new PersistenceOptions()),
            Options.Create(new SessionOptions { ReconnectGraceMs = reconnectGraceMs }),
            simulation,
            LifetimeLog);
        Pickups = new PickupSystem(
            Sessions,
            Persistence,
            sender,
            Lifetime,
            Content,
            Time,
            Options.Create(world),
            simulation,
            PickupLog);
        var tokens = new DevelopmentTokenValidator(Options.Create(authentication), Time);
        var handshake = new HandshakeValidator(compatibility, tokens, Content);
        SessionManager = new SessionManager(
            Inbound,
            Persistence,
            Sessions,
            handshake,
            tokens,
            World,
            sender,
            targeting,
            new PlayerLife(Sessions, sender),
            Lifetime,
            Pickups,
            Time,
            simulation,
            Options.Create(network),
            compatibility,
            Options.Create(world),
            abuse,
            Instruments,
            Log);
        Drops = new ItemDropSystem(World, dropRandom ?? Random, Options.Create(world), simulation);
        Combat = new CombatSystem(
            World,
            Sessions,
            sender,
            targeting,
            Drops,
            new RenewalCombatRules(),
            combatRandom ?? Random,
            Options.Create(world),
            simulation);
        Metrics = new ServerMetrics(
            new TickLogObserver(new CapturingLogger<TickLogObserver>(), new FakeClock()),
            Instruments);
        Status = new StatusPublisher(
            Metrics,
            Inbound,
            Sessions,
            SessionManager,
            World,
            Transport,
            Persistence,
            Clock,
            Instruments,
            simulation);
        var phases = new List<ITickPhase>
        {
            Status,
            new SnapshotPhase(Sessions, sender, Options.Create(world)),
            new VisibilityPhase(Sessions, World, sender, targeting),
            new InventorySyncPhase(Sessions, sender),
            new MovementSystem(Sessions, World, Options.Create(world), simulation),
            Combat,
            Drops,
            SessionManager,
            new RecordingPhase(TickPhase.ApplyCommands, "test", new List<string>(), _ => RunAfterCommands()),
            new CheckpointScheduler(Sessions, Lifetime),
            Pickups
        };
        if (withMonsterAi)
        {
            phases.Add(new MonsterAiSystem(World, Random, Options.Create(world), simulation));
        }

        m_pipeline = new TickPipeline(phases);
        RequiredClientContentVersion = handshake.RequiredClientContentVersion;
    }

    public ServerContent Content { get; }

    public ServerRandom Random { get; }

    public CombatSystem Combat { get; }

    public ItemDropSystem Drops { get; }

    public PickupSystem Pickups { get; }

    public InMemoryServerTransport Transport { get; }

    public InboundQueue Inbound { get; }

    public SessionRegistry Sessions { get; }

    public WorldSimulation World { get; }

    public SessionManager SessionManager { get; }

    public ServerMetrics Metrics { get; }

    public StatusPublisher Status { get; }

    public CapturingLogger<SessionManager> Log { get; }

    /// <summary>
    ///     The store the server writes to: in memory unless a test supplied another, such as PostgreSQL.
    /// </summary>
    public IGameStore GameStore { get; }

    public InMemoryGameStore Store => GameStore as InMemoryGameStore
        ?? throw new InvalidOperationException("This server runs on a real database, not the in-memory store.");

    public CharacterLifetime Lifetime { get; }

    /// <summary>
    ///     The real writer, run on the test thread: <see cref="Tick" /> first lets it finish every job queued so far,
    ///     so a job queued in one tick completes at the start of the next.
    /// </summary>
    public PersistenceWorker Persistence { get; }

    public CapturingLogger<PersistenceWorker> PersistenceLog { get; }

    public CapturingLogger<CharacterLifetime> LifetimeLog { get; } = new();

    public ServerInstruments Instruments { get; } = TestInstruments.Create();

    /// <summary>
    ///     The server's monotonic clock: every tick advances it by one tick's length, so the per-peer budgets refill
    ///     in simulated time. Advance it further to age the published status.
    /// </summary>
    public FakeClock Clock { get; } = new();

    public CapturingLogger<PickupSystem> PickupLog { get; } = new();

    /// <summary>
    ///     When false, <see cref="Tick" /> leaves queued database work alone, as a slow database would.
    /// </summary>
    public bool RunsPersistence { get; set; } = true;

    public FakeTimeProvider Time { get; }

    public uint RequiredClientContentVersion { get; }

    public uint CurrentTick => m_tick;

    public uint Tick(int count = 1)
    {
        for (int index = 0; index < count; index++)
        {
            m_tick++;
            Clock.Advance(TimeSpan.FromSeconds(1.0 / TickRate));
            if (RunsPersistence)
            {
                Persistence.RunUntilIdle();
            }

            m_pipeline.Execute(new TickContext(m_tick, 1f / TickRate));
        }

        return m_tick;
    }

    /// <summary>
    ///     Runs once in the next tick, after sessions have handled their input and before visibility is computed.
    /// </summary>
    public void AfterCommandsOnce(Action action)
    {
        m_afterCommands.Add(action);
    }

    public ConnectionId Connect()
    {
        m_lastConnection++;
        var connection = new ConnectionId(m_lastConnection);
        Inbound.OnConnected(connection);
        return connection;
    }

    public void Disconnect(ConnectionId connection)
    {
        Inbound.OnDisconnected(connection);
    }

    public void SendHello(ConnectionId connection)
    {
        SendHello(
            connection,
            ProtocolConstants.ProtocolVersion,
            BuildVersion,
            RequiredClientContentVersion,
            DevelopmentToken);
    }

    public void SendHello(
        ConnectionId connection,
        ushort protocolVersion,
        string buildVersion,
        uint contentVersion,
        string token)
    {
        var hello = new ClientHello(protocolVersion, buildVersion, contentVersion, token);
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    public void SendMove(ConnectionId connection, uint sequence, float directionX, float directionZ)
    {
        SendMove(connection, sequence, sequence, directionX, directionZ);
    }

    public void SendMove(ConnectionId connection, uint sequence, uint clientTick, float directionX, float directionZ)
    {
        byte[] payload = new byte[MoveInput.EncodedLength];
        new MoveInput(new MoveIntent(sequence, clientTick, directionX, directionZ)).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Input, payload);
    }

    public void SendStop(ConnectionId connection, uint sequence)
    {
        byte[] payload = new byte[StopMovement.EncodedLength];
        new StopMovement(sequence, sequence).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Input, payload);
    }

    public void SendAttack(ConnectionId connection, EntityId target, uint commandSequence)
    {
        byte[] payload = new byte[AttackEntity.EncodedLength];
        new AttackEntity(target, commandSequence).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    public void SendCancel(ConnectionId connection, uint commandSequence)
    {
        byte[] payload = new byte[CancelAction.EncodedLength];
        new CancelAction(commandSequence).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    public void SendRespawn(ConnectionId connection, uint commandSequence)
    {
        byte[] payload = new byte[Respawn.EncodedLength];
        new Respawn(commandSequence).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    public ClientSession SessionOf(ConnectionId connection)
    {
        Sessions.TryGet(connection, out ClientSession? session);
        return session!;
    }

    public void SendTarget(ConnectionId connection, EntityId target)
    {
        byte[] payload = new byte[TargetEntity.EncodedLength];
        new TargetEntity(target).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    /// <summary>
    ///     The monsters of the one map, nearest to <paramref name="position" /> first.
    /// </summary>
    public IReadOnlyList<MonsterEntity> MonstersNear(WorldPosition position)
    {
        var monsters = new List<MonsterEntity>(World.Maps.First().Monsters);
        monsters.Sort((left, right) => Distance(left.Position, position).CompareTo(Distance(right.Position, position)));
        return monsters;
    }

    public void SendEnterWorld(ConnectionId connection, long character)
    {
        byte[] payload = new byte[EnterWorldRequest.EncodedLength];
        new EnterWorldRequest(new CharacterId(character)).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    /// <summary>
    ///     Sends a hello and ticks until the account lookup it starts has answered: the session is then signed in, or
    ///     closed. The hello's tick queues the lookup and the next tick applies its result.
    /// </summary>
    public void SignIn(ConnectionId connection, string token = DevelopmentToken)
    {
        SendHello(connection, ProtocolConstants.ProtocolVersion, BuildVersion, RequiredClientContentVersion, token);
        TickUntil(() => !Sessions.TryGet(connection, out ClientSession? session)
            || (session!.State != SessionState.Authenticating
                && session.State != SessionState.AwaitingHello));
    }

    /// <summary>
    ///     Connects, signs in as its own account, creates the character <c>Tester&lt;n&gt;</c> with ID
    ///     <paramref name="character" /> when the account has none, enters the world, and returns after the tick that
    ///     sent <c>WorldEntered</c>.
    /// </summary>
    public ConnectionId EnterWorld(long character)
    {
        ConnectionId connection = Connect();
        SignInWithCharacter(connection, character);
        SendEnterWorld(connection, character);
        TickUntil(() => SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    /// <summary>
    ///     Signs in as the account <c>dev:tester&lt;n&gt;</c> and makes sure it owns the character
    ///     <paramref name="character" />, named <c>Tester&lt;n&gt;</c>, without entering the world.
    /// </summary>
    public void SignInWithCharacter(ConnectionId connection, long character)
    {
        SignIn(connection, $"{DevelopmentToken}{character}");
        TickUntil(() => SessionOf(connection).Characters != null);
        if (SessionOf(connection).Characters!.All(owned => owned.Id != character))
        {
            Store.NextCharacterId = character;
            SendCreateCharacter(connection, $"Tester{character}");
            TickUntil(() => SessionOf(connection).Characters!.Any(owned => owned.Id == character));
        }
    }

    /// <summary>
    ///     Connects, signs in as <c>dev:&lt;identity&gt;</c>, creates the character <paramref name="name" /> unless the
    ///     account has it, and enters the world with it, whatever store the server uses.
    /// </summary>
    public ConnectionId EnterWorldAs(string identity, string name)
    {
        ConnectionId connection = Connect();
        SignIn(connection, $"dev:{identity}");
        TickUntil(() => SessionOf(connection).Characters != null);
        if (SessionOf(connection).Characters!.All(owned => owned.Name != name))
        {
            SendCreateCharacter(connection, name);
            TickUntil(() => SessionOf(connection).Characters!.Any(owned => owned.Name == name));
        }

        long character = SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
        SendEnterWorld(connection, character);
        TickUntil(() => SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    public void SendInventoryResync(ConnectionId connection)
    {
        byte[] payload = new byte[InventoryResyncRequest.EncodedLength];
        new InventoryResyncRequest().Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    public void SendPickup(ConnectionId connection, EntityId drop, uint commandSequence)
    {
        byte[] payload = new byte[PickupItem.EncodedLength];
        new PickupItem(drop, commandSequence).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    public void SendLogout(ConnectionId connection, uint commandSequence)
    {
        byte[] payload = new byte[Logout.EncodedLength];
        new Logout(commandSequence).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    public void SendCreateCharacter(ConnectionId connection, string name)
    {
        var message = new CreateCharacter(name);
        byte[] payload = new byte[message.GetEncodedLength()];
        message.Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    /// <summary>
    ///     Ticks up to and including the next tick on which the status is published (once per second).
    /// </summary>
    public void TickUntilPublished()
    {
        ServerStatus before = Status.Current;
        for (int ticks = 0; ticks < TickRate; ticks++)
        {
            Tick();
            if (!ReferenceEquals(Status.Current, before))
            {
                return;
            }
        }
    }

    /// <summary>
    ///     Ticks until <paramref name="condition" /> holds, at most 20 ticks, and fails the test otherwise.
    /// </summary>
    public void TickUntil(Func<bool> condition)
    {
        for (int ticks = 0; ticks < 20; ticks++)
        {
            Tick();
            if (condition())
            {
                return;
            }
        }

        throw new InvalidOperationException("The server did not reach the expected state within 20 ticks.");
    }

    public PlayerEntity PlayerOf(ConnectionId connection)
    {
        Sessions.TryGet(connection, out ClientSession? session);
        return session!.Player!;
    }

    private void RunAfterCommands()
    {
        foreach (Action action in m_afterCommands)
        {
            action();
        }

        m_afterCommands.Clear();
    }

    public void Place(ConnectionId connection, float x, float z)
    {
        PlayerEntity player = PlayerOf(connection);
        player.Position = new WorldPosition(x, player.Position.Y, z);
    }

    private static float Distance(WorldPosition from, WorldPosition to)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static ServerContent WithoutMonsterSpawns(ServerContent content)
    {
        var maps = new Dictionary<MapDefinitionId, MapDefinition>();
        foreach (MapDefinition map in content.Maps.Values)
        {
            maps.Add(
                map.Id,
                new MapDefinition(
                    map.Id,
                    map.DisplayName,
                    map.SpawnPosition,
                    map.SpawnFacing,
                    Array.Empty<MonsterSpawn>(),
                    map.Navigation));
        }

        return new ServerContent(
            content.ServerContentVersion,
            content.ClientContentVersion,
            new Dictionary<ItemDefinitionId, ItemDefinition>(content.Items),
            new Dictionary<MonsterDefinitionId, MonsterDefinition>(content.Monsters),
            new Dictionary<SkillDefinitionId, SkillDefinition>(content.Skills),
            new Dictionary<JobDefinitionId, JobDefinition>(content.Jobs),
            maps);
    }
}
}
