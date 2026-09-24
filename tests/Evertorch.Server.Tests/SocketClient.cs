using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Evertorch.Client;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A client over a real UDP socket, composed the way <c>GameClient</c> composes it: the LiteNetLib transport behind
///     the simulated link, the connection, and once in the world the controller, auto-attack, pickup, and driver. The
///     test's own thread plays the Unity frame loop: it polls, runs whole ticks at the server's rate from a real clock,
///     and makes the requests the player's keys would make.
/// </summary>
internal sealed class SocketClient : IDisposable
{
    private const string ConnectionKey = "evertorch";
    private const int DisconnectTimeoutMilliseconds = 5000;
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private readonly LiteNetLibClientTransport m_socket;
    private readonly AutoEnter m_selection;
    private readonly Stopwatch m_clock = Stopwatch.StartNew();
    private readonly TargetCycler m_targetCycler = new();
    private readonly List<PickCandidate> m_targetCandidates = new();
    private ClientWorld? m_world;
    private MovementController? m_controller;
    private AutoAttackState? m_autoAttack;
    private PickupState? m_pickup;
    private LocalPlayerDriver? m_driver;
    private FixedTickClock? m_ticks;
    private double m_lastSeconds;

    public SocketClient(ServerContent content, string identity, string characterName)
    {
        m_socket = new LiteNetLibClientTransport(ConnectionKey, DisconnectTimeoutMilliseconds);
        Link = new LossyTransport(m_socket, 4, () => m_clock.Elapsed.TotalSeconds);
        Connection = new ClientConnection(
            Link,
            new ClientConnectionSettings(
                CompatibilityOptions.DefaultBuildVersion,
                content.ClientContentVersion,
                $"dev:{identity}"),
            new ContentMaps(content));
        m_selection = new AutoEnter(Connection, characterName);
    }

    public LossyTransport Link { get; }

    public ClientConnection Connection { get; }

    public ClientWorld World => Connection.World ?? throw new InvalidOperationException("Not in the world.");

    public MovementController Controller =>
        m_controller ?? throw new InvalidOperationException("Not in the world.");

    public AutoAttackState AutoAttack => m_autoAttack ?? throw new InvalidOperationException("Not in the world.");

    public PickupState Pickup => m_pickup ?? throw new InvalidOperationException("Not in the world.");

    public void Dispose()
    {
        m_socket.Dispose();
    }

    public void Connect(int port)
    {
        Connection.Connect("127.0.0.1", port);
    }

    /// <summary>
    ///     Connects, lets the character selection enter the named character (creating it when the account has none by
    ///     that name), and waits for the world and its inventory baseline.
    /// </summary>
    public void EnterWorld(int port)
    {
        Connect(port);
        bool isEntered = PumpUntil(() => Connection.World?.Inventory.IsCurrent == true);
        Assert.That(isEntered, Is.True, $"entered the world: {Connection.LocalError} {Connection.DisconnectCause}");
    }

    public void Disconnect()
    {
        Connection.Disconnect();
        bool isClosed = PumpUntil(() => Connection.State == ClientConnectionState.Disconnected);
        Assert.That(isClosed, Is.True, "the connection closed");
    }

    public bool PumpUntil(Func<bool> condition)
    {
        return PumpUntil(condition, Limit);
    }

    public bool PumpUntil(Func<bool> condition, TimeSpan limit)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < limit)
        {
            Pump();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    public void PumpFor(TimeSpan duration)
    {
        PumpUntil(() => false, duration);
    }

    /// <summary>
    ///     One frame: what arrived, then the selection, then whole client ticks for the time that passed.
    /// </summary>
    public void Pump()
    {
        Connection.Poll();
        m_selection.Poll();
        ClientWorld? world = Connection.World;
        if (world == null)
        {
            return;
        }

        if (world != m_world)
        {
            EnterMap(world);
        }

        double now = m_clock.Elapsed.TotalSeconds;
        float delta = (float)(now - m_lastSeconds);
        m_lastSeconds = now;
        int due = m_ticks!.Advance(delta);
        for (int index = 0; index < due; index++)
        {
            m_driver!.Tick(m_ticks.NextTick());
        }

        world.Advance(delta);
    }

    /// <summary>
    ///     Tab or Shift+Tab: requests the next or previous live monster this client knows. The target changes only
    ///     when the server confirms it.
    /// </summary>
    public EntityId CycleTarget(bool isForward)
    {
        ClientWorld world = World;
        m_targetCandidates.Clear();
        world.CollectTargetCandidates(m_targetCandidates);
        EntityId next = m_targetCycler.Choose(m_targetCandidates, world.Predictor.Position, world.Target, isForward);
        if (next != default)
        {
            Connection.SendTarget(next);
        }

        return next;
    }

    /// <summary>
    ///     The gamepad West button: auto-attacks the confirmed target.
    /// </summary>
    public void AttackTarget()
    {
        Pickup.Cancel();
        AutoAttack.Attack(World.Target);
    }

    /// <summary>
    ///     F: walks up to the nearest drawn drop within reach and picks it up. Returns the drop, or default.
    /// </summary>
    public EntityId PickUpNearest()
    {
        ClientWorld world = World;
        EntityId drop = world.NearestDrop(world.Predictor.Position, PickupState.KeyReach);
        if (drop != default)
        {
            AutoAttack.OnWalkRequested();
            Pickup.Pickup(drop);
        }

        return drop;
    }

    /// <summary>
    ///     Horizontal distance from the predicted position.
    /// </summary>
    public float DistanceTo(WorldPosition position)
    {
        WorldPosition predicted = World.Predictor.Position;
        float deltaX = predicted.X - position.X;
        float deltaZ = predicted.Z - position.Z;
        return (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private void EnterMap(ClientWorld world)
    {
        m_world = world;
        m_controller = new MovementController(world.Grid);
        m_autoAttack = new AutoAttackState(world, m_controller, Connection, 1.0 / Connection.ServerTickRate);
        m_pickup = new PickupState(world, m_controller, Connection);
        m_driver = new LocalPlayerDriver(
            m_controller,
            new MoveIntentProducer(),
            world,
            Connection,
            m_autoAttack,
            m_pickup);
        m_ticks = new FixedTickClock(1f / Connection.ServerTickRate);
        m_lastSeconds = m_clock.Elapsed.TotalSeconds;
    }

    private sealed class ContentMaps : IMapProvider
    {
        private readonly ServerContent m_content;

        public ContentMaps(ServerContent content)
        {
            m_content = content;
        }

        public bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid)
        {
            bool found = m_content.Maps.TryGetValue(map, out MapDefinition? definition);
            grid = definition?.Navigation;
            return found;
        }
    }
}
}
