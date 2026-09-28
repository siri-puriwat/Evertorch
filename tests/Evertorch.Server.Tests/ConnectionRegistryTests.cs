using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Every transport's connections in one table (System Architecture §8): the IDs, their owners, and the cap.
/// </summary>
[TestFixture]
public sealed class ConnectionRegistryTests
{
    private static ConnectionRegistry Registry(int maxConnections)
    {
        return new ConnectionRegistry(Options.Create(new NetworkOptions { MaxConnections = maxConnections }));
    }

    [Test]
    public void TryAdmit_CountsFromOne_AndRecordsEachOwner()
    {
        ConnectionRegistry connections = Registry(8);
        var udp = new InMemoryServerTransport();
        var web = new InMemoryServerTransport();

        connections.TryAdmit(udp, out ConnectionId first);
        connections.TryAdmit(web, out ConnectionId second);
        connections.TryAdmit(udp, out ConnectionId third);

        Assert.That(new[] { first.Value, second.Value, third.Value }, Is.EqualTo(new[] { 1L, 2L, 3L }));
        Assert.That(connections.TryGetOwner(second, out IServerTransport? owner), Is.True);
        Assert.That(owner, Is.SameAs(web));
        Assert.That(connections.TryGetOwner(new ConnectionId(99), out IServerTransport? _), Is.False);
    }

    [Test]
    public void TryAdmit_FromManyThreadsAtOnce_NeverRepeatsAnId()
    {
        ConnectionRegistry connections = Registry(100_000);
        var owner = new InMemoryServerTransport();
        var ids = new ConcurrentBag<long>();

        Parallel.For(0, 8, _ =>
        {
            for (int index = 0; index < 10_000; index++)
            {
                connections.TryAdmit(owner, out ConnectionId connection);
                ids.Add(connection.Value);
            }
        });

        Assert.That(ids.Distinct().Count(), Is.EqualTo(80_000));
        Assert.That(ids.Max(), Is.EqualTo(80_000));
        Assert.That(connections.Count, Is.EqualTo(80_000));
    }

    [Test]
    public void TryAdmit_OverTheCapAcrossTransports_IsRefused_UntilOneIsReleased()
    {
        ConnectionRegistry connections = Registry(2);
        var udp = new InMemoryServerTransport();
        var web = new InMemoryServerTransport();
        connections.TryAdmit(udp, out ConnectionId first);
        connections.TryAdmit(web, out ConnectionId _);

        bool isOverTheCap = connections.TryAdmit(udp, out ConnectionId _);
        connections.Release(first);
        bool isAfterRelease = connections.TryAdmit(web, out ConnectionId next);

        Assert.That(isOverTheCap, Is.False);
        Assert.That(isAfterRelease, Is.True);
        Assert.That(next.Value, Is.EqualTo(3), "a released ID is never handed out again");
        Assert.That(connections.TryGetOwner(first, out IServerTransport? _), Is.False);
        Assert.That(connections.Count, Is.EqualTo(2));
    }
}
}
