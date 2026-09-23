using System;
using Evertorch.Client;
using Evertorch.Game;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The real client stack (connection, world, prediction, controller, driver) behind a seeded lossy link, driven by
///     a virtual clock instead of Unity's frame loop.
/// </summary>
internal sealed class SimulatedClient : IMapProvider
{
    private const string BuildVersion = TestServer.BuildVersion;

    private readonly TestServer m_server;
    private readonly MoveIntentProducer m_producer = new();
    private readonly AutoEnter m_selection;
    private LocalPlayerDriver? m_driver;
    private uint m_clientTick;

    public SimulatedClient(TestServer server, long character, int seed, Func<double> now)
    {
        m_server = server;
        Loopback = new LoopbackClientTransport(server);
        Link = new LossyTransport(Loopback, seed, now);
        Connection = new ClientConnection(
            Link,
            new ClientConnectionSettings(
                BuildVersion,
                server.Content.ClientContentVersion,
                TestServer.DevelopmentToken + character),
            this);

        // The character gets the number the test asked for, so server-side lookups by character keep working.
        m_selection = new AutoEnter(Connection, $"Sim{character}", () => server.Store.NextCharacterId = character);
    }

    public LoopbackClientTransport Loopback { get; }

    public LossyTransport Link { get; }

    public ClientConnection Connection { get; }

    public MovementController? Controller { get; private set; }

    public AutoAttackState? AutoAttack { get; private set; }

    public ClientWorld World => Connection.World ?? throw new InvalidOperationException("Not in the world yet.");

    public uint LastSequence => m_producer.LastSequence;

    public bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid)
    {
        bool found = m_server.Content.Maps.TryGetValue(map, out MapDefinition? definition);
        grid = definition?.Navigation;
        return found;
    }

    public void Poll()
    {
        Connection.Poll();
        m_selection.Poll();
        if (Controller == null && Connection.World != null)
        {
            Controller = new MovementController(Connection.World.Grid);
            AutoAttack = new AutoAttackState(Connection.World, Controller, Connection, 1.0 / Connection.ServerTickRate);
            m_driver = new LocalPlayerDriver(Controller, m_producer, Connection.World, Connection, AutoAttack);
        }
    }

    public void Tick()
    {
        m_clientTick++;
        m_driver?.Tick(m_clientTick);
    }

    public float DistanceTo(WorldPosition serverPosition)
    {
        WorldPosition predicted = World.Predictor.Position;
        float deltaX = predicted.X - serverPosition.X;
        float deltaZ = predicted.Z - serverPosition.Z;
        return (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }
}
}
