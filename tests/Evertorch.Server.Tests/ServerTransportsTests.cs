using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Every transport behind one <see cref="IServerTransport" /> (System Architecture §8): what each call reaches.
/// </summary>
[TestFixture]
public sealed class ServerTransportsTests
{
    [SetUp]
    public void Compose()
    {
        m_udp = new InMemoryServerTransport();
        m_web = new InMemoryServerTransport();
        m_connections = new ConnectionRegistry(Options.Create(new NetworkOptions { MaxConnections = 8 }));
        m_transports = new ServerTransports(new IServerTransport[] { m_udp, m_web }, m_connections);
    }

    private InMemoryServerTransport m_udp = null!;
    private InMemoryServerTransport m_web = null!;
    private ConnectionRegistry m_connections = null!;
    private ServerTransports m_transports = null!;

    private static byte[] Despawn()
    {
        byte[] payload = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(1), DespawnReason.Removed).Write(payload);
        return payload;
    }

    private ConnectionId Admit(InMemoryServerTransport owner)
    {
        Assert.That(m_connections.TryAdmit(owner, out ConnectionId connection), Is.True);
        return connection;
    }

    [Test]
    public void CloseAdmission_ClosesEveryTransport()
    {
        m_transports.CloseAdmission();

        Assert.That(m_udp.IsAdmissionOpen, Is.False);
        Assert.That(m_web.IsAdmissionOpen, Is.False);
        Assert.That(m_transports.IsAdmissionOpen, Is.False);
    }

    [Test]
    public void CoolDownAddress_OfAConnectionAlreadyGone_ReachesEveryTransport()
    {
        ConnectionId gone = Admit(m_web);
        m_connections.Release(gone);

        m_transports.CoolDownAddress(gone);

        Assert.That(m_udp.CooledDownAddresses, Does.Contain(gone));
        Assert.That(m_web.CooledDownAddresses, Does.Contain(gone));
    }

    [Test]
    public void CoolDownAddress_OfAKnownConnection_ReachesOnlyItsTransport()
    {
        ConnectionId web = Admit(m_web);

        m_transports.CoolDownAddress(web);

        Assert.That(m_web.CooledDownAddresses, Does.Contain(web));
        Assert.That(m_udp.CooledDownAddresses, Is.Empty);
    }

    [Test]
    public void IsAdmissionOpen_WhileAnyTransportIsClosed_IsFalse()
    {
        m_web.CloseAdmission();

        Assert.That(m_transports.IsAdmissionOpen, Is.False);
    }

    [Test]
    public void SendAndDisconnect_ReachOnlyTheTransportThatOwnsTheConnection()
    {
        ConnectionId udp = Admit(m_udp);
        ConnectionId web = Admit(m_web);

        m_transports.Send(web, Despawn());
        m_transports.Disconnect(udp, DisconnectReason.Kicked, string.Empty);

        Assert.That(m_web.SentTo(web), Has.Count.EqualTo(1));
        Assert.That(m_udp.SentTo(web), Is.Empty);
        Assert.That(m_udp.Disconnects[udp], Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(m_web.Disconnects, Is.Empty);
    }

    [Test]
    public void SendDisconnectAndRoundTrip_OfAnUnknownConnection_ReachNoTransport()
    {
        var unknown = new ConnectionId(99);

        m_transports.Send(unknown, Despawn());
        m_transports.Disconnect(unknown, DisconnectReason.Kicked, string.Empty);
        bool isKnown = m_transports.TryGetRoundTripTime(unknown, out int _);

        Assert.That(m_udp.SentTo(unknown), Is.Empty);
        Assert.That(m_web.SentTo(unknown), Is.Empty);
        Assert.That(m_udp.Disconnects, Is.Empty);
        Assert.That(m_web.Disconnects, Is.Empty);
        Assert.That(isKnown, Is.False);
    }

    [Test]
    public void Statistics_AreTheSumOfEveryTransports()
    {
        TransportStatistics statistics = m_transports.GetStatistics();

        Assert.That(
            new[]
            {
                statistics.BytesReceived, statistics.BytesSent, statistics.PacketsReceived, statistics.PacketsSent,
                statistics.PacketsLost
            },
            Is.EqualTo(new[] { 2000L, 4000L, 20L, 40L, 6L }));
    }

    [Test]
    public void Stop_StopsEveryTransportWithTheReason()
    {
        m_transports.Stop(DisconnectReason.Maintenance, "back soon");

        Assert.That(m_udp.StoppedWith, Is.EqualTo(DisconnectReason.Maintenance));
        Assert.That(m_web.StoppedWith, Is.EqualTo(DisconnectReason.Maintenance));
        Assert.That(m_web.StoppedWithMessage, Is.EqualTo("back soon"));
    }

    [Test]
    public void TryGetRoundTripTime_AsksTheOwner()
    {
        ConnectionId web = Admit(m_web);

        bool isKnown = m_transports.TryGetRoundTripTime(web, out int milliseconds);

        Assert.That(isKnown, Is.True);
        Assert.That(milliseconds, Is.EqualTo(42));
    }
}
}
