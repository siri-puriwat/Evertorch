using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class FixedStepLoopTests
{
    private static readonly TimeSpan TwentyHertzStep = TimeSpan.FromMilliseconds(50);

    [TestCase(20)]
    [TestCase(10)]
    [TestCase(60)]
    public void Run_AtConfiguredTickRate_StartsOneTickPerStep(int tickRate)
    {
        FakeClock clock = new FakeClock();
        List<TimeSpan> starts = new List<TimeSpan>();
        List<float> deltas = new List<float>();
        using CancellationTokenSource stop = new CancellationTokenSource();
        FixedStepLoop loop = CreateLoop(clock, new RecordingObserver(), tickRate, context =>
        {
            starts.Add(clock.Elapsed);
            deltas.Add(context.DeltaSeconds);
            StopAfter(context, 10, stop);
        });

        loop.Run(stop.Token);

        long stepTicks = TimeSpan.TicksPerSecond / tickRate;
        TimeSpan[] expected = Enumerable.Range(0, 10).Select(index => TimeSpan.FromTicks(stepTicks * index)).ToArray();
        Assert.That(starts, Is.EqualTo(expected));
        Assert.That(deltas, Is.All.EqualTo(1f / tickRate));
    }

    [Test]
    public void Run_ForEveryTick_NumbersTicksFromOneWithoutGaps()
    {
        FakeClock clock = new FakeClock();
        List<uint> ticks = new List<uint>();
        using CancellationTokenSource stop = new CancellationTokenSource();
        FixedStepLoop loop = CreateLoop(clock, new RecordingObserver(), 20, context =>
        {
            ticks.Add(context.Tick);
            if (context.Tick == 2)
            {
                clock.Advance(TimeSpan.FromSeconds(3));
            }

            StopAfter(context, 5, stop);
        });

        loop.Run(stop.Token);

        Assert.That(ticks, Is.EqualTo(new uint[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void Run_WhenTickRunsLong_ReportsOverrunAndStartsNextTickImmediately()
    {
        FakeClock clock = new FakeClock();
        RecordingObserver observer = new RecordingObserver();
        List<TimeSpan> starts = new List<TimeSpan>();
        using CancellationTokenSource stop = new CancellationTokenSource();
        FixedStepLoop loop = CreateLoop(clock, observer, 20, context =>
        {
            starts.Add(clock.Elapsed);
            if (context.Tick == 2)
            {
                clock.Advance(TimeSpan.FromMilliseconds(60));
            }

            StopAfter(context, 3, stop);
        });

        loop.Run(stop.Token);

        Assert.That(observer.Overruns, Is.EqualTo(new[] { (2u, TimeSpan.FromMilliseconds(60), 0) }));
        Assert.That(starts[2], Is.EqualTo(TimeSpan.FromMilliseconds(110)));
    }

    [Test]
    public void Run_WhenTickTakesExactlyOneStep_ReportsNoOverrun()
    {
        FakeClock clock = new FakeClock();
        RecordingObserver observer = new RecordingObserver();
        using CancellationTokenSource stop = new CancellationTokenSource();
        FixedStepLoop loop = CreateLoop(clock, observer, 20, context =>
        {
            clock.Advance(TwentyHertzStep);
            StopAfter(context, 3, stop);
        });

        loop.Run(stop.Token);

        Assert.That(observer.Overruns, Is.Empty);
        Assert.That(observer.Completed.Select(entry => entry.Duration), Is.All.EqualTo(TwentyHertzStep));
    }

    [Test]
    public void Run_WhenBehindWithinCatchUpLimit_RunsBackToBackWithoutSkipping()
    {
        FakeClock clock = new FakeClock();
        RecordingObserver observer = new RecordingObserver();
        List<TimeSpan> starts = new List<TimeSpan>();
        using CancellationTokenSource stop = new CancellationTokenSource();
        FixedStepLoop loop = CreateLoop(clock, observer, 20, context =>
        {
            starts.Add(clock.Elapsed);
            if (context.Tick == 1)
            {
                clock.Advance(TimeSpan.FromMilliseconds(150));
            }

            StopAfter(context, 5, stop);
        });

        loop.Run(stop.Token);

        int[] startMilliseconds = starts.Select(start => (int)start.TotalMilliseconds).ToArray();
        Assert.That(startMilliseconds, Is.EqualTo(new[] { 0, 150, 150, 150, 200 }));
        Assert.That(observer.Overruns.Select(entry => entry.SkippedSteps), Is.All.EqualTo(0));
    }

    [Test]
    public void Run_WhenFarBehind_AbandonsBacklogAndReportsSkippedSteps()
    {
        FakeClock clock = new FakeClock();
        RecordingObserver observer = new RecordingObserver();
        List<TimeSpan> starts = new List<TimeSpan>();
        using CancellationTokenSource stop = new CancellationTokenSource();
        FixedStepLoop loop = CreateLoop(clock, observer, 20, context =>
        {
            starts.Add(clock.Elapsed);
            if (context.Tick == 1)
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
            }

            StopAfter(context, 3, stop);
        });

        loop.Run(stop.Token);

        int[] startMilliseconds = starts.Select(start => (int)start.TotalMilliseconds).ToArray();
        Assert.That(observer.Overruns, Is.EqualTo(new[] { (1u, TimeSpan.FromMilliseconds(500), 9) }));
        Assert.That(startMilliseconds, Is.EqualTo(new[] { 0, 500, 550 }));
    }

    [Test]
    public void Run_WhenStopRequestedMidTick_FinishesCurrentTickThenReturns()
    {
        FakeClock clock = new FakeClock();
        List<string> log = new List<string>();
        using CancellationTokenSource stop = new CancellationTokenSource();
        ITickPhase[] phases =
        {
            new RecordingPhase(TickPhase.DrainCommands, "drain", log, _ => stop.Cancel()),
            new RecordingPhase(TickPhase.SchedulePersistence, "persist", log),
        };
        FixedStepLoop loop = CreateLoop(clock, new RecordingObserver(), 20, phases);

        loop.Run(stop.Token);

        Assert.That(log, Is.EqualTo(new[] { "drain@1", "persist@1" }));
    }

    [Test]
    public void Run_WhenStopRequestedBeforeStart_ExecutesNoTick()
    {
        RecordingObserver observer = new RecordingObserver();
        List<string> log = new List<string>();
        using CancellationTokenSource stop = new CancellationTokenSource();
        stop.Cancel();
        FixedStepLoop loop = CreateLoop(
            new FakeClock(),
            observer,
            20,
            new RecordingPhase(TickPhase.Movement, "move", log));

        loop.Run(stop.Token);

        Assert.That(log, Is.Empty);
        Assert.That(observer.Completed, Is.Empty);
    }

    [Test]
    public void Run_WhenPhaseThrows_PropagatesToCaller()
    {
        using CancellationTokenSource stop = new CancellationTokenSource();
        FixedStepLoop loop = CreateLoop(
            new FakeClock(),
            new RecordingObserver(),
            20,
            _ => throw new InvalidOperationException("boom"));
        Action run = () => loop.Run(stop.Token);

        Assert.That(run, Throws.InvalidOperationException);
    }

    [TestCase(0)]
    [TestCase(-20)]
    public void Constructor_WhenTickRateIsNotPositive_Throws(int tickRate)
    {
        Action create = () => _ = CreateLoop(new FakeClock(), new RecordingObserver(), tickRate);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    private static void StopAfter(TickContext context, uint lastTick, CancellationTokenSource stop)
    {
        if (context.Tick >= lastTick)
        {
            stop.Cancel();
        }
    }

    private static FixedStepLoop CreateLoop(
        FakeClock clock,
        RecordingObserver observer,
        int tickRate,
        Action<TickContext> onTick)
    {
        RecordingPhase phase = new RecordingPhase(TickPhase.Movement, "tick", new List<string>(), onTick);
        return CreateLoop(clock, observer, tickRate, phase);
    }

    private static FixedStepLoop CreateLoop(
        FakeClock clock,
        RecordingObserver observer,
        int tickRate,
        params ITickPhase[] phases)
    {
        SimulationOptions options = new SimulationOptions { TickRate = tickRate };
        return new FixedStepLoop(clock, new TickPipeline(phases), Options.Create(options), observer);
    }
}
}
