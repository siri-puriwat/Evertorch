using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The one source of connection IDs (System Architecture §8).
/// </summary>
[TestFixture]
public sealed class ConnectionRegistryTests
{
    [Test]
    public void Allocate_CountsFromOne()
    {
        var connections = new ConnectionRegistry();

        long[] ids = Enumerable.Range(0, 3).Select(_ => connections.Allocate().Value).ToArray();

        Assert.That(ids, Is.EqualTo(new[] { 1L, 2L, 3L }));
    }

    [Test]
    public void Allocate_FromManyThreadsAtOnce_NeverRepeatsAnId()
    {
        var connections = new ConnectionRegistry();
        var ids = new ConcurrentBag<long>();

        Parallel.For(0, 8, _ =>
        {
            for (int index = 0; index < 10_000; index++)
            {
                ids.Add(connections.Allocate().Value);
            }
        });

        Assert.That(ids, Has.Count.EqualTo(80_000));
        Assert.That(ids.Distinct().Count(), Is.EqualTo(80_000));
        Assert.That(ids.Max(), Is.EqualTo(80_000));
    }
}
}
