using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server.Tests
{
/// <summary>
/// The real session, world, and visibility code wired to an in-memory transport and ticked by hand. Nothing here
/// stands in for server logic; only the socket and the clock are replaced.
/// </summary>
internal sealed class TestServer
{
    public const string BuildVersion = CompatibilityOptions.DefaultBuildVersion;
    public const string DevelopmentToken = "dev:tester";
    public const int TickRate = 20;

    private static readonly Lazy<ServerContent> RepositoryContent =
        new Lazy<ServerContent>(() => ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage()));

    private readonly TickPipeline m_pipeline;
    private readonly List<Action> m_afterCommands = new List<Action>();
    private long m_lastConnection;
    private uint m_tick;

    public TestServer(
        bool isDevelopmentAuthenticationEnabled = true,
        int handshakeTimeoutMs = 5000,
        int maxInboundEvents = 4096,
        float interestCellSize = 16f,
        int interestNeighborRadius = 1,
        int inputHoldTimeoutMs = 250,
        int maxQueuedInputs = 8,
        int snapshotIntervalTicks = 1)
    {
        Content = RepositoryContent.Value;
        NetworkOptions network = new NetworkOptions
        {
            HandshakeTimeoutMs = handshakeTimeoutMs,
            MaxInboundEvents = maxInboundEvents,
        };
        WorldOptions world = new WorldOptions
        {
            InterestCellSize = interestCellSize,
            InterestNeighborRadius = interestNeighborRadius,
            InputHoldTimeoutMs = inputHoldTimeoutMs,
            MaxQueuedInputs = maxQueuedInputs,
            SnapshotIntervalTicks = snapshotIntervalTicks,
        };
        DevelopmentAuthenticationOptions authentication = new DevelopmentAuthenticationOptions
        {
            Enabled = isDevelopmentAuthenticationEnabled,
        };
        IOptions<CompatibilityOptions> compatibility = Options.Create(new CompatibilityOptions());
        IOptions<SimulationOptions> simulation = Options.Create(new SimulationOptions { TickRate = TickRate });

        Transport = new InMemoryServerTransport();
        Inbound = new InboundQueue(Options.Create(network));
        Sessions = new SessionRegistry();
        World = new WorldSimulation(
            Content,
            Options.Create(world),
            new RenewalCharacterRules(),
            new RenewalMovementRules());
        Log = new CapturingLogger<SessionManager>();
        Time = new FakeTimeProvider();

        MessageSender sender = new MessageSender(Transport);
        HandshakeValidator handshake = new HandshakeValidator(compatibility, Options.Create(authentication), Content);
        SessionManager = new SessionManager(
            Inbound,
            Sessions,
            handshake,
            World,
            sender,
            Time,
            simulation,
            Options.Create(network),
            compatibility,
            Options.Create(world),
            Log);
        m_pipeline = new TickPipeline(
            new ITickPhase[]
            {
                new SnapshotPhase(Sessions, sender, Options.Create(world)),
                new VisibilityPhase(Sessions, sender),
                new MovementSystem(Sessions, Options.Create(world), simulation),
                SessionManager,
                new RecordingPhase(TickPhase.ApplyCommands, "test", new List<string>(), _ => RunAfterCommands()),
            });
        RequiredClientContentVersion = handshake.RequiredClientContentVersion;
    }

    public ServerContent Content { get; }

    public InMemoryServerTransport Transport { get; }

    public InboundQueue Inbound { get; }

    public SessionRegistry Sessions { get; }

    public WorldSimulation World { get; }

    public SessionManager SessionManager { get; }

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
    /// Runs once in the next tick, after sessions have handled their input and before visibility is computed.
    /// </summary>
    public void AfterCommandsOnce(Action action)
    {
        m_afterCommands.Add(action);
    }

    public ConnectionId Connect()
    {
        m_lastConnection++;
        ConnectionId connection = new ConnectionId(m_lastConnection);
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
        ClientHello hello = new ClientHello(protocolVersion, buildVersion, contentVersion, token);
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

    public void SendEnterWorld(ConnectionId connection, long character)
    {
        byte[] payload = new byte[EnterWorldRequest.EncodedLength];
        new EnterWorldRequest(new CharacterId(character)).Write(payload);
        Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
    }

    /// <summary>
    /// Connects, completes the handshake, enters the world, and runs the tick that processes all three.
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
}
}
