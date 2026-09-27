using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A client over a real UDP socket, composed the way <c>GameClient</c> composes it: the LiteNetLib transport behind
///     the simulated link, the connection, and for each map the controller, auto-attack, pickup, skill, talk, and
///     driver, with the movement sequence and the client tick carried across a map change. The test's own thread plays the
///     Unity frame loop: it polls, runs whole ticks at the server's rate from a real clock, and makes the requests the
///     player's keys would make.
/// </summary>
internal sealed class SocketClient : IDisposable
{
    private const string ConnectionKey = "evertorch";
    private const int DisconnectTimeoutMilliseconds = 5000;
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    // The whole answer, its members in their order: nothing more may appear in it.
    private static readonly Regex AnswerShape = new(
        @"^\{""transport"":""udp"",""host"":""(?<host>[^""]+)"",""port"":(?<port>[0-9]+),"
        + @"""token"":""(?<token>[A-Za-z0-9_-]{43})""\}$",
        RegexOptions.CultureInvariant);

    private readonly LiteNetLibClientTransport m_socket;
    private readonly ServerContent m_content;
    private readonly AutoEnter m_selection;
    private readonly Stopwatch m_clock = Stopwatch.StartNew();
    private readonly TargetCycler m_targetCycler = new();
    private readonly List<PickCandidate> m_targetCandidates = new();
    private readonly List<EntityId> m_npcWindows = new();
    private ClientWorld? m_world;
    private MovementController? m_controller;
    private AutoAttackState? m_autoAttack;
    private PickupState? m_pickup;
    private SkillState? m_skill;
    private TalkState? m_talk;
    private LocalPlayerDriver? m_driver;
    private MoveIntentProducer? m_producer;
    private FixedTickClock? m_ticks;
    private WorldDirection m_held;
    private double m_lastSeconds;

    public SocketClient(ServerContent content, string identity, string characterName)
        : this(content, characterName, $"dev:{identity}", 0)
    {
    }

    // The last parameter only tells this constructor from the public one.
    private SocketClient(ServerContent content, string characterName, string token, int _)
    {
        m_content = content;
        m_socket = new LiteNetLibClientTransport(ConnectionKey, DisconnectTimeoutMilliseconds);
        Link = new LossyTransport(m_socket, 4, () => m_clock.Elapsed.TotalSeconds);
        Connection = new ClientConnection(
            Link,
            new ClientConnectionSettings(
                ProtocolConstants.BuildVersion,
                content.ClientContentVersion,
                token),
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

    public SkillState Skill => m_skill ?? throw new InvalidOperationException("Not in the world.");

    public TalkState Talk => m_talk ?? throw new InvalidOperationException("Not in the world.");

    /// <summary>
    ///     Each NPC whose window opened, in order: where <c>GameClient</c> opens its NPC window.
    /// </summary>
    public IReadOnlyList<EntityId> NpcWindows => m_npcWindows;

    public void Dispose()
    {
        m_socket.Dispose();
    }

    /// <summary>
    ///     A client whose hello carries a session token the gateway issued.
    /// </summary>
    public static SocketClient WithToken(ServerContent content, string token, string characterName)
    {
        return new SocketClient(content, characterName, token, 0);
    }

    /// <summary>
    ///     Signs in at the gateway as a native client does (Network Protocol §4), through a client that pins the test's
    ///     certificate, and checks the request's and the answer's JSON byte for byte.
    /// </summary>
    public static GatewaySignInResult SignIn(TestCertificate certificate, int gatewayPort, string login,
        string password)
    {
        using HttpClient http = certificate.CreateClient();
        string request = TestCertificate.SignInJson(login, password);
        Assert.That(
            request,
            Is.EqualTo($"{{\"login\":\"{login}\",\"password\":\"{password}\",\"transports\":[\"udp\"]}}"));
        using HttpResponseMessage response = http
            .PostAsync($"https://127.0.0.1:{gatewayPort}/session", TestCertificate.JsonContent(request))
            .GetAwaiter()
            .GetResult();
        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Match answer = AnswerShape.Match(body);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "signed in at the gateway");
        Assert.That(answer.Success, Is.True, $"the answer's JSON: {body}");
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        var signIn = new GatewaySignInResult(
            answer.Groups["host"].Value,
            int.Parse(answer.Groups["port"].Value, CultureInfo.InvariantCulture),
            answer.Groups["token"].Value);
        Assert.That(
            SignInAnswer.TryCreate(SignInAnswer.UdpTransport, signIn.Host, signIn.Port, signIn.Token, out _),
            Is.True,
            "the client's own check takes the answer");
        return signIn;
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
            // As in GameClient, only a new entry starts the movement sequence and the client tick again; a map change
            // goes from one world straight to the next.
            m_producer = null;
            m_ticks = null;
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
    ///     A held key or stick: unlike a direction set on <see cref="Controller" />, it outlives a map change, as
    ///     <c>GameClient</c> reads the held key again every frame. A zero direction lets go.
    /// </summary>
    public void Hold(float directionX, float directionZ)
    {
        m_held = new WorldDirection(directionX, directionZ);
        Controller.SetManualDirection(directionX, directionZ);
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
        Talk.Cancel();
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
            Talk.Cancel();
            Pickup.Pickup(drop);
        }

        return drop;
    }

    /// <summary>
    ///     A click or tap on an NPC: walks up to it, and its window opens on arrival. Talking replaces whatever else
    ///     the character was doing.
    /// </summary>
    public void TalkTo(EntityId npc)
    {
        AutoAttack.OnWalkRequested();
        Pickup.Cancel();
        Skill.Cancel();
        Talk.Talk(npc);
    }

    /// <summary>
    ///     E or the D-pad right: talks to the nearest drawn NPC. Returns it, or default when none is in view.
    /// </summary>
    public EntityId TalkToNearest()
    {
        EntityId npc = World.NearestNpc(World.Predictor.Position);
        if (npc != default)
        {
            TalkTo(npc);
        }

        return npc;
    }

    /// <summary>
    ///     A skill-bar key: the skill on the caster, or at the confirmed target once the approach is in range. False
    ///     when the skill state starts nothing.
    /// </summary>
    public bool UseSkill(SkillDefinitionId skill)
    {
        Pickup.Cancel();
        Talk.Cancel();
        return Skill.Use(skill, m_content.Skills[skill].TargetType);
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
        m_controller.SetManualDirection(m_held.X, m_held.Z);
        m_autoAttack = new AutoAttackState(world, m_controller, Connection, 1.0 / Connection.ServerTickRate);
        m_pickup = new PickupState(world, m_controller, Connection);
        m_skill = new SkillState(world, m_controller, Connection, 1.0 / Connection.ServerTickRate);
        m_talk = new TalkState(world, m_controller);
        m_talk.Arrived += m_npcWindows.Add;
        m_producer ??= new MoveIntentProducer();
        m_driver = new LocalPlayerDriver(
            m_controller,
            m_producer,
            world,
            Connection,
            m_autoAttack,
            m_pickup,
            m_skill,
            m_talk);
        m_ticks ??= new FixedTickClock(1f / Connection.ServerTickRate);
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

/// <summary>
///     What the gateway answered a sign-in: where to connect, and the token for the hello.
/// </summary>
internal sealed class GatewaySignInResult
{
    public GatewaySignInResult(string host, int port, string token)
    {
        Host = host;
        Port = port;
        Token = token;
    }

    public string Host { get; }

    public int Port { get; }

    public string Token { get; }
}
}
