using System;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class TickLogObserverTests
{
    [Test]
    public void OnOverrun_ForFirstOverrun_LogsWarningWithStableEventName()
    {
        var logger = new CapturingLogger<TickLogObserver>();
        var observer = new TickLogObserver(logger, new FakeClock());

        observer.OnOverrun(12, TimeSpan.FromMilliseconds(80), 0);

        Assert.That(logger.Entries, Has.Count.EqualTo(1));
        Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(logger.Entries[0].EventId.Name, Is.EqualTo("TickOverrun"));
        Assert.That(logger.Entries[0].Message, Does.Contain("Tick 12"));
    }

    [Test]
    public void OnOverrun_RepeatedWithinInterval_LogsOnceThenReportsSuppressedCount()
    {
        var logger = new CapturingLogger<TickLogObserver>();
        var clock = new FakeClock();
        var observer = new TickLogObserver(logger, clock);

        observer.OnOverrun(1, TimeSpan.FromMilliseconds(80), 0);
        observer.OnOverrun(2, TimeSpan.FromMilliseconds(80), 0);
        observer.OnOverrun(3, TimeSpan.FromMilliseconds(80), 0);
        clock.Advance(TimeSpan.FromSeconds(5));
        observer.OnOverrun(4, TimeSpan.FromMilliseconds(80), 0);

        Assert.That(logger.Entries, Has.Count.EqualTo(2));
        Assert.That(logger.Entries[1].Message, Does.Contain("Tick 4").And.Contain("2 earlier overruns"));
    }

    [Test]
    public void OnTickCompleted_ForOrdinaryTick_LogsNothing()
    {
        var logger = new CapturingLogger<TickLogObserver>();
        var observer = new TickLogObserver(logger, new FakeClock());

        observer.OnTickCompleted(1, TimeSpan.FromMilliseconds(2));

        Assert.That(logger.Entries, Is.Empty);
    }
}
}
