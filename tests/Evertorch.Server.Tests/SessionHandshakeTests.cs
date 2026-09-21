using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class SessionHandshakeTests
{
    [Test]
    public void Hello_WithSupportedVersions_ReceivesServerHelloWithTickRateAndRequiredContentVersion()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick();

        InMemoryServerTransport.SentMessage sent = server.Transport.SentTo(connection).Single();
        Assert.That(ServerHello.TryRead(sent.Payload, out ServerHello? hello), Is.True);
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(sent.Delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        Assert.That(hello!.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
        Assert.That(hello.ServerBuildVersion, Is.EqualTo(TestServer.BuildVersion));
        Assert.That(hello.ServerTickRate, Is.EqualTo((uint)TestServer.TickRate));
        Assert.That(hello.RequiredClientContentVersion, Is.EqualTo(server.RequiredClientContentVersion));
        Assert.That(hello.ServerTimeUnixMilliseconds, Is.EqualTo(server.Time.UtcNow.ToUnixTimeMilliseconds()));
        Assert.That(server.Transport.Disconnects, Is.Empty);
    }

    [Test]
    public void Hello_RequiredContentVersion_IsTheFirstEightDigitsOfTheLoadedClientVersion()
    {
        TestServer server = new TestServer();

        ContentVersionCodec.TryToWire(server.Content.ClientContentVersion, out uint expected);

        Assert.That(server.RequiredClientContentVersion, Is.EqualTo(expected));
    }

    [Test]
    public void Hello_WithProtocolMismatch_IsDisconnectedWithProtocolMismatch()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(
            connection,
            2,
            TestServer.BuildVersion,
            server.RequiredClientContentVersion,
            TestServer.DevelopmentToken);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ProtocolMismatch);
    }

    [Test]
    public void Hello_WithUnsupportedBuild_IsDisconnectedWithClientBuildUnsupported()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(
            connection,
            ProtocolConstants.ProtocolVersion,
            "0.1.0",
            server.RequiredClientContentVersion,
            TestServer.DevelopmentToken);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ClientBuildUnsupported);
    }

    [Test]
    public void Hello_WithStaleContent_IsDisconnectedWithContentUpdateRequired()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(
            connection,
            ProtocolConstants.ProtocolVersion,
            TestServer.BuildVersion,
            server.RequiredClientContentVersion + 1,
            TestServer.DevelopmentToken);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ContentUpdateRequired);
    }

    [Test]
    public void Hello_WithSeveralIncompatibilities_ReportsTheMostGeneralFirst()
    {
        TestServer server = new TestServer(isDevelopmentAuthenticationEnabled: false);
        ConnectionId connection = server.Connect();
        server.SendHello(connection, 9, "0.1.0", 0, "nonsense");

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.ProtocolMismatch);
    }

    [Test]
    public void Hello_WhenDevelopmentAuthenticationDisabled_IsDisconnectedWithAuthenticationFailed()
    {
        TestServer server = new TestServer(isDevelopmentAuthenticationEnabled: false);
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
    }

    [TestCase("")]
    [TestCase("dev:")]
    [TestCase("tester")]
    [TestCase("DEV:tester")]
    [TestCase("dev:has space")]
    [TestCase("dev:semi;colon")]
    [TestCase("dev:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Hello_WithTokenThatIsNotADevelopmentIdentity_IsDisconnectedWithAuthenticationFailed(string token)
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(
            connection,
            ProtocolConstants.ProtocolVersion,
            TestServer.BuildVersion,
            server.RequiredClientContentVersion,
            token);

        server.Tick();

        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
    }

    [Test]
    public void Hello_SentTwice_IsAnsweredOnce()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);
        server.SendHello(connection);

        server.Tick();

        Assert.That(server.Transport.OpcodesSentTo(connection), Is.EqualTo(new[] { MessageOpcode.ServerHello }));
        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(server.Transport.Disconnects, Is.Empty);
    }

    [Test]
    public void Hello_NotSentWithinTimeout_IsDisconnected()
    {
        TestServer server = new TestServer(handshakeTimeoutMs: 500);
        ConnectionId connection = server.Connect();

        server.Tick(10);
        bool wasStillConnected = server.Transport.Disconnects.Count == 0;
        server.Tick();

        Assert.That(wasStillConnected, Is.True, "500 ms at 20 Hz is ten ticks after the connect tick");
        AssertRefused(server, connection, DisconnectReason.AuthenticationFailed);
    }

    [Test]
    public void Hello_SentInTime_IsNotExpiredLater()
    {
        TestServer server = new TestServer(handshakeTimeoutMs: 500);
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick(40);

        Assert.That(server.Transport.Disconnects, Is.Empty);
        Assert.That(server.Sessions.TryGet(connection, out _), Is.True);
    }

    [Test]
    public void Log_ForAnyHandshakeOutcome_NeverContainsTheToken()
    {
        const string SecretToken = "dev:very-secret-identity";
        TestServer server = new TestServer();
        ConnectionId accepted = server.Connect();
        ConnectionId refused = server.Connect();
        server.SendHello(
            accepted,
            ProtocolConstants.ProtocolVersion,
            TestServer.BuildVersion,
            server.RequiredClientContentVersion,
            SecretToken);
        server.SendHello(refused, 7, TestServer.BuildVersion, server.RequiredClientContentVersion, SecretToken);
        server.SendEnterWorld(accepted, 5);

        server.Tick();

        Assert.That(server.Log.Entries, Is.Not.Empty);
        Assert.That(
            server.Log.Entries.Select(entry => entry.Message),
            Has.None.Contains("very-secret-identity").And.None.Contains("dev:"));
    }

    [Test]
    public void Disconnect_BeforeHello_RemovesTheSessionQuietly()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.Tick();
        server.Disconnect(connection);

        server.Tick();

        Assert.That(server.Sessions.Sessions, Is.Empty);
        Assert.That(server.Transport.Disconnects, Is.Empty);
    }

    private static void AssertRefused(TestServer server, ConnectionId connection, DisconnectReason reason)
    {
        Assert.That(server.Transport.Disconnects, Does.ContainKey(connection));
        Assert.That(server.Transport.Disconnects[connection], Is.EqualTo(reason));
        Assert.That(server.Transport.SentTo(connection), Is.Empty);
        Assert.That(server.Sessions.TryGet(connection, out _), Is.False);
    }
}
}
