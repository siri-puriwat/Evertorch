using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
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
    public const string BuildVersion = CompatibilityOptions.DefaultBuildVersion;
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
        int itemDropLifetimeMs = 60000)
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
        Inbound = new InboundQueue(Options.Create(network));
        Sessions = new SessionRegistry();
        World = new WorldSimulation(
            Content,
            Options.Create(world),
            new RenewalCharacterRules(),
            new RenewalMovementRules(),
            Random);
        Log = new CapturingLogger<SessionManager>();
        Time = new FakeTimeProvider();

        var sender = new MessageSender(Transport);
        var targeting = new Targeting(sender);
        var handshake = new HandshakeValidator(compatibility, Options.Create(authentication), Content);
        SessionManager = new SessionManager(
            Inbound,
            Sessions,
            handshake,
            World,
            sender,
            targeting,
            new PlayerLife(Sessions, sender),
            Time,
            simulation,
            Options.Create(network),
            compatibility,
            Options.Create(world),
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
        Metrics = new ServerMetrics(new TickLogObserver(new CapturingLogger<TickLogObserver>(), new FakeClock()));
        Status = new StatusPublisher(Metrics, Inbound, Sessions, SessionManager, World, Transport, simulation);
        var phases = new List<ITickPhase>
        {
            Status,
            new SnapshotPhase(Sessions, sender, Options.Create(world)),
            new VisibilityPhase(Sessions, World, sender, targeting),
            new MovementSystem(Sessions, World, Options.Create(world), simulation),
            Combat,
            Drops,
            SessionManager,
            new RecordingPhase(TickPhase.ApplyCommands, "test", new List<string>(), _ => RunAfterCommands())
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

    public InMemoryServerTransport Transport { get; }

    public InboundQueue Inbound { get; }

    public SessionRegistry Sessions { get; }

    public WorldSimulation World { get; }

    public SessionManager SessionManager { get; }

    public ServerMetrics Metrics { get; }

    public StatusPublisher Status { get; }

    public CapturingLogger<SessionManager> Log { get; }

    public FakeTimeProvider Time { get; }

    public uint RequiredClientContentVersion { get; }

    public uint CurrentTick => m_tick;

    public uint Tick(int count = 1)
    {
        for (int index = 0; index < count; index++)
        {
            m_tick++;
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
    ///     Connects, completes the handshake, enters the world, and runs the tick that processes all three.
    /// </summary>
    public ConnectionId EnterWorld(long character)
    {
        ConnectionId connection = Connect();
        SendHello(connection);
        SendEnterWorld(connection, character);
        Tick();
        return connection;
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
