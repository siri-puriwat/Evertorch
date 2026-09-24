using System;
using System.Net;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The per-address connection limits (Network Protocol §11), on an injected clock.
/// </summary>
[TestFixture]
public sealed class AddressThrottleTests
{
    private static readonly IPAddress Home = IPAddress.Parse("198.51.100.7");
    private static readonly IPAddress Away = IPAddress.Parse("203.0.113.9");

    private static AddressThrottle Create(
        FakeClock clock,
        int perSecond = 2,
        int maxConnections = 64,
        int maxAddresses = 10000,
        bool isEnabled = true,
        int cooldownMs = 60000)
    {
        return new AddressThrottle(
            Options.Create(
                new AbuseOptions
                {
                    Enabled = isEnabled,
                    ConnectionRequestsPerSecond = perSecond,
                    MaxConnectionsPerAddress = maxConnections,
                    MaxTrackedAddresses = maxAddresses,
                    KickCooldownMs = cooldownMs
                }),
            clock);
    }

    private static IPAddress Numbered(int index)
    {
        return new IPAddress(new byte[] { 10, 0, (byte)(index / 256), (byte)(index % 256) });
    }

    [Test]
    public void Connections_AtTheCapForAnAddress_AreRefusedUntilOneEnds()
    {
        var clock = new FakeClock();
        AddressThrottle throttle = Create(clock, 100, 2);
        throttle.OnConnected(Home);
        throttle.OnConnected(Home);

        bool isAdmitted = throttle.TryAdmit(Home, out string limit);
        throttle.OnDisconnected(Home);

        Assert.That(isAdmitted, Is.False);
        Assert.That(limit, Is.EqualTo(ServerInstruments.AddressConnectionsLimit));
        Assert.That(throttle.TryAdmit(Home, out string _), Is.True);
        Assert.That(throttle.TryAdmit(Away, out string _), Is.True, "other addresses are not affected");
    }

    [Test]
    public void Cooldown_CoversItsAddressUntilItEnds()
    {
        var clock = new FakeClock();
        AddressThrottle throttle = Create(clock, cooldownMs: 1000);
        throttle.OnConnected(Home);

        throttle.StartCooldown(Home);
        bool whileCooling = throttle.IsCoolingDown(Home);
        clock.Advance(TimeSpan.FromMilliseconds(999));
        bool justBefore = throttle.IsCoolingDown(Home);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.That(whileCooling, Is.True);
        Assert.That(justBefore, Is.True);
        Assert.That(throttle.IsCoolingDown(Home), Is.False);
        Assert.That(throttle.IsCoolingDown(Away), Is.False, "other addresses are not affected");
    }

    [Test]
    public void Cooldown_OfZeroOrWithTheLimitsOff_CoversNothing()
    {
        var clock = new FakeClock();
        AddressThrottle none = Create(clock, cooldownMs: 0);
        AddressThrottle off = Create(clock, isEnabled: false);

        none.StartCooldown(Home);
        off.StartCooldown(Home);

        Assert.That(none.IsCoolingDown(Home), Is.False);
        Assert.That(off.IsCoolingDown(Home), Is.False);
    }

    [Test]
    public void Cooldown_SurvivesTheTableMakingRoom()
    {
        var clock = new FakeClock();
        AddressThrottle throttle = Create(clock, 100, maxAddresses: 16);
        throttle.StartCooldown(Home);
        for (int index = 0; index < 15; index++)
        {
            throttle.TryAdmit(Numbered(index), out string _);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        bool isAdmitted = throttle.TryAdmit(Away, out string _);

        Assert.That(isAdmitted, Is.True, "idle addresses made room");
        Assert.That(throttle.IsCoolingDown(Home), Is.True, "the cooling address was kept");
    }

    [Test]
    public void Requests_FasterThanTheRate_AreRefusedUntilTheBudgetRefills()
    {
        var clock = new FakeClock();
        AddressThrottle throttle = Create(clock);

        bool first = throttle.TryAdmit(Home, out string _);
        bool second = throttle.TryAdmit(Home, out string _);
        bool third = throttle.TryAdmit(Home, out string limit);
        clock.Advance(TimeSpan.FromSeconds(0.5));
        bool later = throttle.TryAdmit(Home, out string _);

        Assert.That(new[] { first, second, third, later }, Is.EqualTo(new[] { true, true, false, true }));
        Assert.That(limit, Is.EqualTo(ServerInstruments.AddressRateLimit));
        Assert.That(throttle.TryAdmit(Away, out string _), Is.True, "other addresses are not affected");
    }

    [Test]
    public void Table_WhenFull_ForgetsIdleAddressesToMakeRoom()
    {
        var clock = new FakeClock();
        AddressThrottle throttle = Create(clock, maxAddresses: 16);
        for (int index = 0; index < 16; index++)
        {
            throttle.TryAdmit(Numbered(index), out string _);
            throttle.OnConnected(Numbered(index));
        }

        bool isFullRefused = !throttle.TryAdmit(Home, out string _);
        throttle.OnDisconnected(Numbered(3));
        clock.Advance(TimeSpan.FromSeconds(1));
        bool isAdmittedAfterRoom = throttle.TryAdmit(Home, out string _);

        Assert.That(isFullRefused, Is.True, "no address could make room while every one held a connection");
        Assert.That(isAdmittedAfterRoom, Is.True);
    }

    [Test]
    public void TryAdmit_WithTheLimitsOff_AlwaysAdmits()
    {
        AddressThrottle throttle = Create(new FakeClock(), 1, 1, isEnabled: false);
        throttle.OnConnected(Home);

        for (int request = 0; request < 50; request++)
        {
            Assert.That(throttle.TryAdmit(Home, out string _), Is.True);
        }
    }
}
}
