using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class RateLimitedLogTests
{
    private static int[] EnterFromManyThreads(RateLimitedLog log, int threads, int callsPerThread)
    {
        using var start = new ManualResetEventSlim(false);
        Task<int>[] workers = Enumerable.Range(0, threads)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                int entered = 0;
                for (int call = 0; call < callsPerThread; call++)
                {
                    if (log.TryEnter(out int _))
                    {
                        entered++;
                    }
                }

                return entered;
            }))
            .ToArray();
        start.Set();
        return Task.WhenAll(workers).GetAwaiter().GetResult();
    }

    [Test]
    public void TryEnter_AfterTheInterval_LetsTheNextThroughWithTheHeldBackCount()
    {
        var clock = new FakeClock();
        var log = new RateLimitedLog(clock, TimeSpan.FromSeconds(5));

        bool first = log.TryEnter(out int none);
        log.TryEnter(out int _);
        log.TryEnter(out int _);
        clock.Advance(TimeSpan.FromSeconds(5));
        bool next = log.TryEnter(out int suppressed);

        Assert.That(first, Is.True);
        Assert.That(none, Is.Zero);
        Assert.That(next, Is.True);
        Assert.That(suppressed, Is.EqualTo(2));
    }

    [Test]
    public void TryEnter_FromManyThreadsAtOnce_LetsExactlyOneThroughAndCountsEveryOther()
    {
        var clock = new FakeClock();
        var log = new RateLimitedLog(clock, TimeSpan.FromSeconds(5));

        int[] entered = EnterFromManyThreads(log, 8, 20000);
        clock.Advance(TimeSpan.FromSeconds(5));
        log.TryEnter(out int suppressed);

        Assert.That(entered.Sum(), Is.EqualTo(1));
        Assert.That(suppressed, Is.EqualTo(8 * 20000 - 1));
    }

    [Test]
    public void TryEnter_WithSeveralPerInterval_LetsThatManyThroughEachInterval()
    {
        var clock = new FakeClock();
        var log = new RateLimitedLog(clock, TimeSpan.FromSeconds(1), 3);

        int entered = 0;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            entered += log.TryEnter(out int _) ? 1 : 0;
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        bool next = log.TryEnter(out int suppressed);

        Assert.That(entered, Is.EqualTo(3));
        Assert.That(next, Is.True);
        Assert.That(suppressed, Is.EqualTo(7));
    }

    [Test]
    public void TryEnter_WithinTheInterval_HoldsBack()
    {
        var clock = new FakeClock();
        var log = new RateLimitedLog(clock, TimeSpan.FromSeconds(5));

        log.TryEnter(out int _);
        clock.Advance(TimeSpan.FromSeconds(4.9));
        bool held = log.TryEnter(out int suppressed);

        Assert.That(held, Is.False);
        Assert.That(suppressed, Is.Zero);
    }
}
}
