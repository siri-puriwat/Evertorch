using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     One already-decoded thing that happened on a connection, carried from a network thread to the tick thread.
/// </summary>
public readonly struct InboundEvent
{
    private InboundEvent(
        InboundEventKind kind,
        ConnectionId connection,
        ClientHello? hello,
        EnterWorldRequest enterWorld,
        MoveIntent intent,
        EntityId target = default,
        uint commandSequence = 0,
        string? name = null)
    {
        Kind = kind;
        Connection = connection;
        Hello = hello;
        EnterWorld = enterWorld;
        Intent = intent;
        Target = target;
        CommandSequence = commandSequence;
        Name = name;
    }

    public InboundEventKind Kind { get; }

    public ConnectionId Connection { get; }

    public ClientHello? Hello { get; }

    public EnterWorldRequest EnterWorld { get; }

    public MoveIntent Intent { get; }

    public EntityId Target { get; }

    public uint CommandSequence { get; }

    public string? Name { get; }

    public static InboundEvent Connected(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Connected, connection, null, default, default);
    }

    public static InboundEvent Disconnected(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Disconnected, connection, null, default, default);
    }

    public static InboundEvent Malformed(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Malformed, connection, null, default, default);
    }

    public static InboundEvent RateLimited(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.RateLimited, connection, null, default, default);
    }

    public static InboundEvent ForHello(ConnectionId connection, ClientHello hello)
    {
        return new InboundEvent(InboundEventKind.Hello, connection, hello, default, default);
    }

    public static InboundEvent ForEnterWorld(ConnectionId connection, EnterWorldRequest request)
    {
        return new InboundEvent(InboundEventKind.EnterWorld, connection, null, request, default);
    }

    public static InboundEvent ForMove(ConnectionId connection, MoveIntent intent)
    {
        return new InboundEvent(InboundEventKind.Move, connection, null, default, intent);
    }

    public static InboundEvent ForTarget(ConnectionId connection, EntityId target)
    {
        return new InboundEvent(InboundEventKind.Target, connection, null, default, default, target);
    }

    public static InboundEvent ForAttack(ConnectionId connection, EntityId target, uint commandSequence)
    {
        return new InboundEvent(InboundEventKind.Attack, connection, null, default, default, target, commandSequence);
    }

    public static InboundEvent ForPickup(ConnectionId connection, EntityId drop, uint commandSequence)
    {
        return new InboundEvent(InboundEventKind.Pickup, connection, null, default, default, drop, commandSequence);
    }

    public static InboundEvent ForCreateCharacter(ConnectionId connection, string name)
    {
        return new InboundEvent(InboundEventKind.CreateCharacter, connection, null, default, default, name: name);
    }

    public static InboundEvent ForInventoryResync(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.InventoryResync, connection, null, default, default);
    }

    public static InboundEvent ForCommand(InboundEventKind kind, ConnectionId connection, uint commandSequence)
    {
        return new InboundEvent(kind, connection, null, default, default, default, commandSequence);
    }
}
}
