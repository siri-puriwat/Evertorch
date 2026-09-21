using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
/// One already-decoded thing that happened on a connection, carried from a network thread to the tick thread.
/// </summary>
public readonly struct InboundEvent
{
    private InboundEvent(
        InboundEventKind kind,
        ConnectionId connection,
        ClientHello? hello,
        EnterWorldRequest enterWorld)
    {
        Kind = kind;
        Connection = connection;
        Hello = hello;
        EnterWorld = enterWorld;
    }

    public InboundEventKind Kind { get; }

    public ConnectionId Connection { get; }

    public ClientHello? Hello { get; }

    public EnterWorldRequest EnterWorld { get; }

    public static InboundEvent Connected(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Connected, connection, null, default);
    }

    public static InboundEvent Disconnected(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Disconnected, connection, null, default);
    }

    public static InboundEvent Malformed(ConnectionId connection)
    {
        return new InboundEvent(InboundEventKind.Malformed, connection, null, default);
    }

    public static InboundEvent ForHello(ConnectionId connection, ClientHello hello)
    {
        return new InboundEvent(InboundEventKind.Hello, connection, hello, default);
    }

    public static InboundEvent ForEnterWorld(ConnectionId connection, EnterWorldRequest request)
    {
        return new InboundEvent(InboundEventKind.EnterWorld, connection, null, request);
    }
}
}
