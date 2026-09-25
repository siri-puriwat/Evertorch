using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Natural regeneration at 20 Hz (Gameplay Systems §2.1 and the experience research note): a level 1 adventurer
///     regains 2 HP every 120 ticks and 1 SP every 160 ticks, counted from the tick it was placed.
/// </summary>
[TestFixture]
public sealed class RegenerationTests
{
    private const int HealthTicks = 120;
    private const int SpiritTicks = 160;

    private static uint[] HealthSentTo(TestServer server, ConnectionId connection)
    {
        return HealthMessagesSentTo(server, connection).Select(health => health.Current).ToArray();
    }

    private static CharacterHealth[] HealthMessagesSentTo(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CharacterHealth)
            .Select(message => CharacterHealth.TryRead(message.Payload, out CharacterHealth read) ? read : default)
            .ToArray();
    }

    [Test]
    public void Respawn_RestoresSpInFull()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentSpirit = 3;
        server.Combat.Kill(server.World.Maps.Single(), player, null, server.CurrentTick);
        server.Tick();

        server.SendRespawn(connection, 1);
        server.Tick();

        Assert.That(player.IsDead, Is.False);
        Assert.That(player.CurrentSpirit, Is.EqualTo(player.MaxSpirit));
    }

    [Test]
    public void Tick_AtFullHpAndSp_ChangesAndSendsNothing()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        server.Transport.ClearSent();

        server.Tick(2 * SpiritTicks);

        Assert.That(player.CurrentHealth, Is.EqualTo(player.MaxHealth));
        Assert.That(player.CurrentSpirit, Is.EqualTo(player.MaxSpirit));
        Assert.That(HealthSentTo(server, connection), Is.Empty);
    }

    [Test]
    public void Tick_ForARetainedCharacter_RegainsWithoutSendingAnything()
    {
        var server = new TestServer(reconnectGraceMs: 60_000);
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentHealth = 10;
        server.Disconnect(connection);
        server.Tick();
        server.Transport.ClearSent();

        server.Tick(HealthTicks - 1);

        Assert.That(player.CurrentHealth, Is.EqualTo(12));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
    }

    [Test]
    public void Tick_NeverRaisesHpOrSpAboveTheirMaximums()
    {
        var server = new TestServer();
        PlayerEntity player = server.PlayerOf(server.EnterWorld(1));
        player.CurrentHealth = player.MaxHealth - 1;
        player.CurrentSpirit = player.MaxSpirit - 1;

        server.Tick(SpiritTicks);

        Assert.That(player.CurrentHealth, Is.EqualTo(player.MaxHealth));
        Assert.That(player.CurrentSpirit, Is.EqualTo(player.MaxSpirit));
    }

    [Test]
    public void Tick_RegainsHpOnEachStepFromTheSpawn_AndSendsIt()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentHealth = 10;
        server.Transport.ClearSent();

        server.Tick(HealthTicks - 1);
        Assert.That(player.CurrentHealth, Is.EqualTo(10), "one tick before the first step");
        server.Tick();
        Assert.That(player.CurrentHealth, Is.EqualTo(12), "6000 ms after the spawn");
        server.Tick(HealthTicks);
        Assert.That(player.CurrentHealth, Is.EqualTo(14), "12000 ms after the spawn");

        Assert.That(HealthSentTo(server, connection), Is.EqualTo(new[] { 12u, 14u }));
    }

    [Test]
    public void Tick_RegainsSpOnEachStepFromTheSpawn()
    {
        var server = new TestServer();
        PlayerEntity player = server.PlayerOf(server.EnterWorld(1));
        player.CurrentSpirit = 0;

        server.Tick(SpiritTicks - 1);
        Assert.That(player.CurrentSpirit, Is.Zero, "one tick before the first step");
        server.Tick();
        Assert.That(player.CurrentSpirit, Is.EqualTo(1), "8000 ms after the spawn");
        server.Tick(SpiritTicks);
        Assert.That(player.CurrentSpirit, Is.EqualTo(2), "16000 ms after the spawn");
    }

    [Test]
    public void Tick_SendsTheOwnerItsSpStep_AndNobodyElse()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        ConnectionId other = server.EnterWorld(2);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentSpirit = 0;
        server.Transport.ClearSent();

        server.Tick(SpiritTicks);

        CharacterHealth sent = HealthMessagesSentTo(server, connection).Single();
        Assert.That(sent.CurrentSpirit, Is.EqualTo(1u));
        Assert.That(sent.MaximumSpirit, Is.EqualTo((uint)player.MaxSpirit));
        Assert.That(sent.Current, Is.EqualTo((uint)player.CurrentHealth));
        Assert.That(HealthMessagesSentTo(server, other), Is.Empty);
    }

    [Test]
    public void Tick_WhenHpAndSpStepTogether_SendsOneMessage()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        server.Tick(4 * HealthTicks - 1);
        player.CurrentHealth = 10;
        player.CurrentSpirit = 0;
        server.Transport.ClearSent();

        server.Tick();

        CharacterHealth sent = HealthMessagesSentTo(server, connection).Single();
        Assert.That((sent.Current, sent.CurrentSpirit), Is.EqualTo((12u, 1u)), "24 s after the spawn, both at once");
    }

    [Test]
    public void Tick_WhileDead_RegainsNothing_AndTheScheduleGoesOnAfterTheRespawn()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        server.Tick(10);
        server.Combat.Kill(server.World.Maps.Single(), player, null, server.CurrentTick);

        server.Tick(HealthTicks - 10);
        Assert.That(player.CurrentHealth, Is.Zero, "dead through the first step");

        server.SendRespawn(connection, 1);
        server.Tick();
        Assert.That(player.IsDead, Is.False);
        player.CurrentHealth = 10;
        server.Tick(HealthTicks - 2);
        Assert.That(player.CurrentHealth, Is.EqualTo(10), "one tick before the second step");
        server.Tick();
        Assert.That(player.CurrentHealth, Is.EqualTo(12), "the second step, still counted from the spawn");
    }

    [Test]
    public void Tick_WhileWalking_StillRegains()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentHealth = 10;
        server.Tick(HealthTicks - 3);

        for (uint sequence = 1; sequence <= 3; sequence++)
        {
            server.SendMove(connection, sequence, 1f, 0f);
            server.Tick();
        }

        Assert.That((player.StateFlags & EntityStateFlags.Moving) != 0, Is.True, "walking at the step");
        Assert.That(player.CurrentHealth, Is.EqualTo(12));
    }
}
}
