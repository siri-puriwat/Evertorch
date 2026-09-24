using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The <c>Evertorch.Server</c> meter, read the way a collector reads it: through a <see cref="MeterListener" />
///     (System Architecture §10).
/// </summary>
[TestFixture]
public sealed class ServerInstrumentsTests
{
    private static readonly string[] AllowedTagKeys =
    {
        "map",
        "direction",
        "operation",
        "outcome",
        "state",
        "limit",
        "violation",
        "reason"
    };

    private static IReadOnlyList<Recorded> Named(MeterRecorder recorder, string instrument)
    {
        return recorder.Measurements.Where(measurement => measurement.Instrument == instrument).ToArray();
    }

    private static double Single(MeterRecorder recorder, string instrument)
    {
        return Named(recorder, instrument).Single().Value;
    }

    private static double Tagged(MeterRecorder recorder, string instrument, string key, string value)
    {
        return Named(recorder, instrument)
            .Single(measurement => measurement.Tags.Any(tag => tag.Key == key && Equals(tag.Value, value)))
            .Value;
    }

    private static double SumTagged(MeterRecorder recorder, string instrument, string key, string value)
    {
        return Named(recorder, instrument)
            .Where(measurement => measurement.Tags.Any(tag => tag.Key == key && Equals(tag.Value, value)))
            .Sum(measurement => measurement.Value);
    }

    private static void SendGarbage(TestServer server, ConnectionId connection, int count)
    {
        for (int index = 0; index < count; index++)
        {
            server.Inbound.OnPayload(connection, ProtocolChannel.Control, new byte[] { 0xFF, 0x7F });
        }
    }

    private sealed class Recorded
    {
        public Recorded(string instrument, double value, KeyValuePair<string, object?>[] tags)
        {
            Instrument = instrument;
            Value = value;
            Tags = tags;
        }

        public string Instrument { get; }

        public double Value { get; }

        public KeyValuePair<string, object?>[] Tags { get; }
    }

    /// <summary>
    ///     Listens to one meter only, so measurements of other servers built by the same test run stay out.
    /// </summary>
    private sealed class MeterRecorder : IDisposable
    {
        private readonly MeterListener m_listener = new();
        private readonly List<Recorded> m_measurements = new();

        public MeterRecorder(Meter meter)
        {
            m_listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter == meter)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            m_listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Add(instrument, value, tags.ToArray()));
            m_listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Add(instrument, value, tags.ToArray()));
            m_listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
                Add(instrument, value, tags.ToArray()));
            m_listener.Start();
        }

        public IReadOnlyList<Recorded> Measurements
        {
            get
            {
                lock (m_measurements)
                {
                    return m_measurements.ToArray();
                }
            }
        }

        public void Dispose()
        {
            m_listener.Dispose();
        }

        public void Observe()
        {
            m_listener.RecordObservableInstruments();
        }

        private void Add(Instrument instrument, double value, KeyValuePair<string, object?>[] tags)
        {
            lock (m_measurements)
            {
                m_measurements.Add(new Recorded(instrument.Name, value, tags));
            }
        }
    }

    [Test]
    public void AuthenticationFailure_IsCountedOnceAndPublished()
    {
        var server = new TestServer();
        using var recorder = new MeterRecorder(server.Instruments.Meter);
        ConnectionId connection = server.Connect();

        server.SendHello(
            connection,
            ProtocolConstants.ProtocolVersion,
            TestServer.BuildVersion,
            server.RequiredClientContentVersion,
            "not-a-development-token");
        server.TickUntilPublished();

        Assert.That(server.Transport.Disconnects[connection], Is.EqualTo(DisconnectReason.AuthenticationFailed));
        Assert.That(Single(recorder, "evertorch.sessions.authentication_failures"), Is.EqualTo(1));
        Assert.That(server.Status.Current.AuthenticationFailures, Is.EqualTo(1));
    }

    [Test]
    public void Gauges_ReadThePublishedStatus()
    {
        var server = new TestServer();
        using var recorder = new MeterRecorder(server.Instruments.Meter);
        server.EnterWorld(7);
        server.Connect();
        server.TickUntilPublished();

        recorder.Observe();

        Assert.That(Single(recorder, "evertorch.sessions.connected"), Is.EqualTo(2));
        Assert.That(Single(recorder, "evertorch.sessions.authenticated"), Is.EqualTo(1));
        Assert.That(Single(recorder, "evertorch.sessions.in_world"), Is.EqualTo(1));
        Assert.That(Tagged(recorder, "evertorch.map.players", "map", "map.training_ground"), Is.EqualTo(1));
        Assert.That(Tagged(recorder, "evertorch.map.entities", "map", "map.training_ground"), Is.EqualTo(1));
        Assert.That(Tagged(recorder, "evertorch.transport.bytes", "direction", "received"), Is.EqualTo(1000));
        Assert.That(Tagged(recorder, "evertorch.transport.bytes", "direction", "sent"), Is.EqualTo(2000));
        Assert.That(Single(recorder, "evertorch.transport.packets_lost"), Is.EqualTo(3));
        Assert.That(Tagged(recorder, "evertorch.persistence.database.state", "state", "available"), Is.EqualTo(1));
        Assert.That(Tagged(recorder, "evertorch.persistence.database.state", "state", "unavailable"), Is.Zero);
        Assert.That(Single(recorder, "evertorch.admission.open"), Is.EqualTo(1));
        Assert.That(Named(recorder, "evertorch.sessions.round_trip_time").Select(rtt => rtt.Value),
            Has.Some.EqualTo(42));
    }

    [Test]
    public void Host_CreatesItsMeterThroughTheMeterFactory()
    {
        using IHost first = TestHosts.CreateBuilder(new[] { "--Network:Port=0" }, AppContext.BaseDirectory).Build();
        using IHost second = TestHosts.CreateBuilder(new[] { "--Network:Port=0" }, AppContext.BaseDirectory).Build();

        Meter meter = first.Services.GetRequiredService<ServerInstruments>().Meter;

        Assert.That(meter.Name, Is.EqualTo(ServerInstruments.MeterName));
        Assert.That(meter.Scope, Is.SameAs(first.Services.GetRequiredService<IMeterFactory>()));
        Assert.That(second.Services.GetRequiredService<ServerInstruments>().Meter, Is.Not.SameAs(meter));
    }

    [Test]
    public void PersistenceJobs_RecordDurationByOperationAndOutcome_AndCountFailuresAndRetries()
    {
        var server = new TestServer();
        using var recorder = new MeterRecorder(server.Instruments.Meter);
        ConnectionId connection = server.EnterWorld(7);
        server.Store.IsUnavailable = true;

        server.Lifetime.QueueCheckpoint(server.SessionOf(connection).Character!);
        server.Tick(2);

        IReadOnlyList<Recorded> jobs = Named(recorder, "evertorch.persistence.job.duration");
        Assert.That(
            jobs.Where(job => job.Tags.Contains(new KeyValuePair<string, object?>("outcome", "succeeded")))
                .SelectMany(job => job.Tags.Where(tag => tag.Key == "operation").Select(tag => tag.Value)),
            Is.SupersetOf(new[] { "authenticate", "list characters", "load character" }));
        Recorded failure = Named(recorder, "evertorch.persistence.job.failures").Single();
        Assert.That(failure.Tags, Does.Contain(new KeyValuePair<string, object?>("operation", "checkpoint")));
        Assert.That(failure.Tags, Does.Contain(new KeyValuePair<string, object?>("outcome", "unavailable")));
        Assert.That(Named(recorder, "evertorch.persistence.retries"), Has.Count.EqualTo(3), "the default retries");
    }

    [Test]
    public void RunningHost_RecordsTheDurationOfItsTicks()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        using IHost host = TestHosts.CreateBuilder(new[] { "--Network:Port=0" }, root.Path).Build();
        using var recorder = new MeterRecorder(host.Services.GetRequiredService<ServerInstruments>().Meter);

        host.Start();
        bool isTicking = SpinWait.SpinUntil(
            () => Named(recorder, "evertorch.tick.duration").Count >= 5,
            TimeSpan.FromSeconds(10));
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isTicking, Is.True, "the loop's observer feeds the tick histogram");
    }

    [Test]
    public void Tags_NeverNameAConnectionAccountOrCharacter()
    {
        var server = new TestServer();
        using var recorder = new MeterRecorder(server.Instruments.Meter);
        server.EnterWorld(7);
        server.EnterWorld(8);
        SendGarbage(server, server.EnterWorld(9), 10);
        server.TickUntilPublished();

        recorder.Observe();

        string[] keys = recorder.Measurements.SelectMany(measurement => measurement.Tags)
            .Select(tag => tag.Key)
            .Distinct()
            .ToArray();
        Assert.That(recorder.Measurements, Is.Not.Empty);
        Assert.That(keys, Is.SubsetOf(AllowedTagKeys));
    }

    [Test]
    public void TickObserver_RecordsDurationsOverrunsAndSkippedSteps()
    {
        ServerInstruments instruments = TestInstruments.Create();
        using var recorder = new MeterRecorder(instruments.Meter);
        var metrics = new ServerMetrics(
            new TickLogObserver(new CapturingLogger<TickLogObserver>(), new FakeClock()),
            instruments);

        metrics.OnTickCompleted(1, TimeSpan.FromMilliseconds(4));
        metrics.OnTickCompleted(2, TimeSpan.FromMilliseconds(9));
        metrics.OnOverrun(3, TimeSpan.FromMilliseconds(400), 3);

        Assert.That(
            Named(recorder, "evertorch.tick.duration").Select(tick => tick.Value),
            Is.EqualTo(new[] { 4.0, 9.0 }));
        Assert.That(Single(recorder, "evertorch.tick.overruns"), Is.EqualTo(1));
        Assert.That(Single(recorder, "evertorch.tick.skipped_steps"), Is.EqualTo(3));
    }

    [Test]
    public void Violations_AreCountedByKind_AndTheirDisconnectsByReason()
    {
        var server = new TestServer();
        using var recorder = new MeterRecorder(server.Instruments.Meter);
        ConnectionId garbage = server.EnterWorld(7);
        ConnectionId attacker = server.EnterWorld(8);
        var defaults = new AbuseOptions();

        SendGarbage(server, garbage, 10);
        for (uint sequence = 1; sequence <= defaults.CombatCommandBurst + 10; sequence++)
        {
            server.SendAttack(attacker, new EntityId(999999), sequence);
        }

        server.Tick();

        Assert.That(SumTagged(recorder, "evertorch.abuse.violations", "violation", "malformed"), Is.EqualTo(10));
        Assert.That(SumTagged(recorder, "evertorch.abuse.violations", "violation", "command_rate"), Is.EqualTo(10));
        Assert.That(SumTagged(recorder, "evertorch.abuse.disconnects", "reason", "kicked"), Is.EqualTo(1));
        Assert.That(SumTagged(recorder, "evertorch.abuse.disconnects", "reason", "rate_limited"), Is.EqualTo(1));
    }
}
}
