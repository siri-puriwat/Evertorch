using System;
using System.Collections.Generic;
using Evertorch.Client;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The client's chat bucket is never looser than the server's (Network Protocol §11; critique finding 1 of the
///     pre-Milestone-12 review): whatever an honest client lets through, even bunched by up to 0.9 s of network delay
///     and rounded to the server's ticks, the server's <c>chat</c> bucket takes, so a typist is never scored.
/// </summary>
[TestFixture]
public sealed class ClientChatBucketTests
{
    private const double MaxDelaySeconds = 0.9;

    // Every line the client let through, sent at its time and arriving at the server in order, each no earlier than
    // the one before, after a delay of up to MaxDelaySeconds; true when the server's bucket took every one.
    private static bool ServerTakesEveryLine(IReadOnlyList<double> sent, IReadOnlyList<double> delays)
    {
        var options = new AbuseOptions();
        var server = new TickBucket(options.ChatCommandsPerSecond, options.ChatCommandBurst, TestServer.TickRate, 0);
        double arrived = 0d;
        for (int index = 0; index < sent.Count; index++)
        {
            arrived = Math.Max(arrived, sent[index] + delays[index]);
            uint tick = (uint)Math.Ceiling(arrived * TestServer.TickRate);
            if (!server.TryTake(tick))
            {
                return false;
            }
        }

        return true;
    }

    private static List<double> LetThrough(IEnumerable<double> attempts)
    {
        var client = new ChatThrottle();
        var sent = new List<double>();
        foreach (double at in attempts)
        {
            if (client.TryTake(at))
            {
                sent.Add(at);
            }
        }

        return sent;
    }

    [Test]
    public void Defaults_AreAtMostTheServersBurstLessOne_AndItsRate()
    {
        var options = new AbuseOptions();

        Assert.That(ChatThrottle.Burst, Is.LessThanOrEqualTo(options.ChatCommandBurst - 1));
        Assert.That(ChatThrottle.PerSecond, Is.LessThanOrEqualTo(options.ChatCommandsPerSecond));
        Assert.That(ChatThrottle.Burst, Is.LessThanOrEqualTo(options.PartyCommandBurst - 1), "the party's too");
        Assert.That(ChatThrottle.PerSecond, Is.LessThanOrEqualTo(options.PartyCommandsPerSecond));
        Assert.That(ChatThrottle.Burst, Is.LessThanOrEqualTo(options.TradeCommandBurst - 1), "the trade's too");
        Assert.That(ChatThrottle.PerSecond, Is.LessThanOrEqualTo(options.TradeCommandsPerSecond));
    }

    [Test]
    public void ManyTypists_AtRandomTimesAndDelays_AreNeverRefusedByTheServer()
    {
        var random = new Random(12);
        for (int typist = 0; typist < 2000; typist++)
        {
            var attempts = new List<double>();
            double at = 0d;
            for (int line = 0; line < 40; line++)
            {
                at += random.NextDouble() * (random.Next(4) == 0 ? 3d : 0.4);
                attempts.Add(at);
            }

            List<double> sent = LetThrough(attempts);
            var delays = new List<double>();
            for (int index = 0; index < sent.Count; index++)
            {
                delays.Add(random.NextDouble() * MaxDelaySeconds);
            }

            Assert.That(ServerTakesEveryLine(sent, delays), Is.True, $"typist {typist}");
        }
    }

    [Test]
    public void TheCheck_WouldCatchABucketAsLooseAsTheServers()
    {
        var options = new AbuseOptions();
        var sent = new List<double>();
        var delays = new List<double>();
        for (int line = 0; line < options.ChatCommandBurst; line++)
        {
            sent.Add(0d);
            delays.Add(MaxDelaySeconds);
        }

        sent.Add(1d);
        delays.Add(0d);

        Assert.That(ServerTakesEveryLine(sent, delays), Is.False, "a burst of five delayed, then a line on time");
    }

    [Test]
    public void TryTake_GivesABurstOfFour_ThenOneASecond()
    {
        var bucket = new ChatThrottle();
        var taken = new List<bool>();
        for (int line = 0; line < 5; line++)
        {
            taken.Add(bucket.TryTake(10d));
        }

        taken.Add(bucket.TryTake(10.9));
        taken.Add(bucket.TryTake(11.01));
        taken.Add(bucket.TryTake(11.5));

        Assert.That(taken, Is.EqualTo(new[] { true, true, true, true, false, false, true, false }));
    }

    [Test]
    public void WorstCaseBunching_TheBurstDelayedAndTheNextLinesNot_IsTakenByTheServer()
    {
        var attempts = new List<double>();
        for (int line = 0; line < 4; line++)
        {
            attempts.Add(0d);
        }

        for (int second = 1; second <= 10; second++)
        {
            attempts.Add(second);
        }

        List<double> sent = LetThrough(attempts);
        var delays = new List<double>();
        for (int index = 0; index < sent.Count; index++)
        {
            delays.Add(sent[index] < 1d ? MaxDelaySeconds : 0d);
        }

        Assert.That(sent, Has.Count.EqualTo(14));
        Assert.That(ServerTakesEveryLine(sent, delays), Is.True);
    }
}
}
