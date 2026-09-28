using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Protocol;
using LiteNetLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using NUnit.Framework;
using DisconnectReason = Evertorch.Protocol.DisconnectReason;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Hostile input at the composed server's real socket (Network Protocol §11, §13): raw datagrams that never make a
///     connection, a connected peer that sends garbage, and the logs they leave behind.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class HostileSocketTests
{
    private const string Key = "evertorch";
    private const int CooldownMs = 2000;
    private const string OversizedSentinel = "secret-oversized-request";

    private static IHost StartHost(TemporaryDirectory root, CapturingLoggerProvider logs, params string[] settings)
    {
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        HostApplicationBuilder builder = TestHosts.CreateBuilder(
            new[]
            {
                "--Network:Port=0",
                "--DevelopmentAuthentication:Enabled=true",
                $"--Abuse:KickCooldownMs={CooldownMs}",
                "--Logging:LogLevel:Default=Debug"
            }.Concat(settings).ToArray(),
            root.Path,
            new InMemoryGameStore());
        builder.Logging.AddProvider(logs);
        builder.Logging.AddFilter<ConsoleLoggerProvider>(null, LogLevel.Warning);
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    // What the client's own library sends to connect, caught on a socket that never answers.
    private static byte[] CaptureConnectRequest()
    {
        using var catcher = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        catcher.Client.ReceiveTimeout = 5000;
        int port = ((IPEndPoint)catcher.Client.LocalEndPoint!).Port;
        var manager = new NetManager(new EventBasedNetListener()) { IPv6Enabled = false };
        manager.Start();
        try
        {
            manager.Connect("127.0.0.1", port, Key);
            IPEndPoint? sender = null;
            return catcher.Receive(ref sender);
        }
        finally
        {
            manager.Stop();
        }
    }

    // The oversized requests come first: the address's request budget refuses what follows them unread.
    private static IEnumerable<byte[]> HostileDatagrams(byte[] connectRequest)
    {
        var random = new Random(11);

        // The header of a real request, then data in place of the key, up to nearly the largest datagram loopback
        // carries. The data starts with a word no log may repeat. LiteNetLib reads at most 1432 bytes of a datagram
        // and drops a longer one, so only the first reaches the key check.
        int key = IndexOf(connectRequest, Encoding.UTF8.GetBytes(Key));
        byte[] sentinel = Encoding.ASCII.GetBytes(OversizedSentinel);
        foreach (int size in new[] { 1000, 20000, 60000 })
        {
            byte[] oversized = new byte[key + size];
            connectRequest.AsSpan(0, key).CopyTo(oversized);
            random.NextBytes(oversized.AsSpan(key));
            sentinel.CopyTo(oversized, key);
            yield return oversized;
        }

        for (int index = 0; index < 300; index++)
        {
            byte[] garbage = new byte[random.Next(0, 1400)];
            random.NextBytes(garbage);
            yield return garbage;
        }

        // Every kind of packet header cut short, with and without the fragment flag.
        for (int property = 0; property < 32; property++)
        {
            for (int length = 1; length <= 20; length++)
            {
                byte[] truncated = new byte[length];
                random.NextBytes(truncated);
                truncated[0] = (byte)property;
                yield return truncated;
                byte[] fragmented = (byte[])truncated.Clone();
                fragmented[0] |= 0x80;
                yield return fragmented;
            }
        }

        for (int length = 1; length < connectRequest.Length; length++)
        {
            yield return connectRequest.Take(length).ToArray();
        }
    }

    private static int IndexOf(byte[] data, byte[] part)
    {
        for (int start = 0; start + part.Length <= data.Length; start++)
        {
            if (data.AsSpan(start, part.Length).SequenceEqual(part))
            {
                return start;
            }
        }

        throw new InvalidOperationException("The connection request does not carry the key.");
    }

    private static byte[] Hello(ushort protocolVersion, uint contentVersion, string token)
    {
        var hello = new ClientHello(protocolVersion, ProtocolConstants.BuildVersion, contentVersion, token);
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private static TestNetClient Connected(int port)
    {
        var client = new TestNetClient();
        client.Connect(port, Key);
        Assert.That(client.WaitFor(() => client.IsConnected), Is.True, "the peer connected");
        return client;
    }

    private static void SignIn(TestNetClient client, uint contentVersion, string token)
    {
        client.Send(
            Hello(ProtocolConstants.ProtocolVersion, contentVersion, token),
            ProtocolChannel.Control,
            DeliveryMethod.ReliableOrdered);
        Assert.That(
            client.WaitFor(() => client.Received.Any(message =>
                MessageRouting.TryReadOpcode(message.Payload, out MessageOpcode opcode)
                && opcode == MessageOpcode.ServerHello)),
            Is.True,
            "signed in");
    }

    private static byte[] Cancel(uint commandSequence)
    {
        byte[] payload = new byte[CancelAction.EncodedLength];
        new CancelAction(commandSequence).Write(payload);
        return payload;
    }

    private static void AssertNoSecretIn(IReadOnlyList<string> lines)
    {
        Assert.That(lines.Where(line => line.Contains("secret", StringComparison.OrdinalIgnoreCase)), Is.Empty);
        Assert.That(lines.Where(line => line.Contains("Password", StringComparison.OrdinalIgnoreCase)), Is.Empty);
        Assert.That(lines.Where(line => line.Contains(TestHosts.UnreachableDatabase)), Is.Empty);
    }

    private static void SendGarbage(TestNetClient client, int count)
    {
        for (int index = 0; index < count; index++)
        {
            client.Send(new byte[] { 0xFF, 0x7F, 0x01 }, ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);
        }
    }

    [Test]
    public void Garbage_FromAPeerThatNeverSignedIn_KicksIt_AndItsAddressWaitsOutTheCooldown()
    {
        using var root = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();
        using IHost host = StartHost(root, logs);
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        ServerContent content = host.Services.GetRequiredService<ServerContent>();

        using TestNetClient hostile = Connected(port);
        SendGarbage(hostile, 10);
        Assert.That(hostile.WaitFor(() => hostile.IsDisconnected), Is.True, "the hostile peer was closed");
        var cooling = Stopwatch.StartNew();

        using var early = new SocketClient(content, "hostile-neighbour", "Neighbour1");
        early.Connect(port);
        bool isRefused = early.PumpUntil(() => early.Connection.State == ClientConnectionState.Disconnected);
        TimeSpan refusedAfter = cooling.Elapsed;

        Thread.Sleep(TimeSpan.FromMilliseconds(CooldownMs + 200) - cooling.Elapsed);
        using var later = new SocketClient(content, "hostile-neighbour", "Neighbour1");
        later.EnterWorld(port);

        Assert.That(hostile.Notice!.Reason, Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(isRefused, Is.True, "the same address was refused during the cooldown");
        Assert.That(refusedAfter, Is.LessThan(TimeSpan.FromMilliseconds(CooldownMs)));
        Assert.That(early.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.RateLimited));
        Assert.That(later.Connection.MalformedMessages + later.Connection.UnexpectedMessages, Is.Zero);
        host.StopAsync().GetAwaiter().GetResult();
    }

    [Test]
    public void HostileInput_LeavesNoTokenIdentityOrConnectionStringInTheLogs()
    {
        using var root = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();
        using IHost host = StartHost(root, logs);
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        uint content = host.Services.GetRequiredService<HandshakeValidator>().RequiredClientContentVersion;

        using TestNetClient signedIn = Connected(port);
        SignIn(signedIn, content, "dev:secret-login-one");
        signedIn.Send(
            Hello(ProtocolConstants.ProtocolVersion, content, "dev:secret-login-two"),
            ProtocolChannel.Control,
            DeliveryMethod.ReliableOrdered);
        SendGarbage(signedIn, 10);
        using TestNetClient mismatched = Connected(port);
        mismatched.Send(Hello(1, content, "dev:secret-login-three"), ProtocolChannel.Control,
            DeliveryMethod.ReliableOrdered);
        using TestNetClient forged = Connected(port);
        forged.Send(
            Hello(ProtocolConstants.ProtocolVersion, content, "secret-token-four"),
            ProtocolChannel.Control,
            DeliveryMethod.ReliableOrdered);
        using TestNetClient tooLong = Connected(port);
        tooLong.Send(
            Hello(ProtocolConstants.ProtocolVersion, content, $"dev:secret-{new string('x', 58)}"),
            ProtocolChannel.Control,
            DeliveryMethod.ReliableOrdered);

        // Signed in first: a flood from a connection that has no account would cool the whole address down.
        using TestNetClient flooder = Connected(port);
        SignIn(flooder, content, "dev:secret-login-five");
        for (uint sequence = 1; sequence <= 400; sequence++)
        {
            flooder.Send(Cancel(sequence), ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);
        }

        Assert.That(signedIn.WaitFor(() => signedIn.IsDisconnected), Is.True);
        Assert.That(mismatched.WaitFor(() => mismatched.IsDisconnected), Is.True);
        Assert.That(forged.WaitFor(() => forged.IsDisconnected), Is.True);
        Assert.That(tooLong.WaitFor(() => tooLong.IsDisconnected), Is.True);
        Assert.That(flooder.WaitFor(() => flooder.IsDisconnected), Is.True);
        host.StopAsync().GetAwaiter().GetResult();

        IReadOnlyList<string> lines = logs.Lines;
        Assert.That(signedIn.Notice!.Reason, Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(tooLong.Notice!.Reason, Is.EqualTo(DisconnectReason.AuthenticationFailed), "an identity too long");
        Assert.That(flooder.Notice!.Reason, Is.EqualTo(DisconnectReason.RateLimited), "the control flood");
        Assert.That(lines.Any(line => line.Contains("ViolationDisconnect")), Is.True, "the audit events were written");
        AssertNoSecretIn(lines);
    }

    // Milestone 6's commands from a player in the world: refused, over the item bucket, then malformed until the
    // connection is closed. The identity carries a word no log may repeat.
    [Test]
    public void HostileItemAndSkillCommands_AreRefusedThrottledAndScored_AndLeaveNoSecretInTheLogs()
    {
        using var root = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();
        // A burst small enough that the refusals before the throttle stay within the audit's share of a connection.
        const int itemBurst = 5;
        using IHost host = StartHost(root, logs, $"--Abuse:ItemCommandBurst={itemBurst}");
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int violationsToClose = new AbuseOptions().ViolationThreshold / ViolationScore.Points;
        // A skill and an equip cut short, an unequip of slot 0, and a use of row 0.
        byte[][] malformed =
        {
            new byte[] { 0x08, 0x00, 0x01 },
            new byte[] { 0x10, 0x00, 0x01 },
            new byte[] { 0x11, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 },
            new byte[] { 0x12, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 }
        };

        using var player = new SocketClient(content, "secret-items-identity", "Hostile1");
        player.EnterWorld(port);
        player.Connection.SendUseSkill(new SkillDefinitionId("skill.spark_bolt"), default);
        player.PumpFor(TimeSpan.FromMilliseconds(1100));
        for (int index = 0; index <= itemBurst; index++)
        {
            player.Connection.SendEquip(999999);
        }

        player.PumpFor(TimeSpan.FromMilliseconds(1100));
        for (int index = 0; index < 2 * violationsToClose; index++)
        {
            player.Link.Send(ProtocolChannel.Control, MessageDelivery.ReliableOrdered,
                malformed[index % malformed.Length]);
        }

        bool isClosed = player.PumpUntil(() => player.Connection.State == ClientConnectionState.Disconnected);
        host.StopAsync().GetAwaiter().GetResult();

        IReadOnlyList<string> lines = logs.Lines;
        Assert.That(isClosed, Is.True, "the connection was closed");
        Assert.That(player.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(lines.Any(line => line.Contains("had UseSkill refused")), Is.True, "the refused skill was audited");
        Assert.That(lines.Any(line => line.Contains("had Equip refused")), Is.True, "the refused equip was audited");
        Assert.That(lines.Any(line => line.Contains("sent Equip over the session_item limit")), Is.True);
        Assert.That(lines.Any(line => line.Contains("ViolationDisconnect")), Is.True);
        AssertNoSecretIn(lines);
    }

    [Test]
    public void RawDatagrams_OfGarbageTruncatedHeadersAndOversizedRequests_LeaveTheServerServingHonestClients()
    {
        using var root = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();
        using IHost host = StartHost(root, logs);
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        InboundQueue inbound = host.Services.GetRequiredService<InboundQueue>();
        byte[] connectRequest = CaptureConnectRequest();

        int sent = 0;
        using (var attacker = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            var server = new IPEndPoint(IPAddress.Loopback, port);
            foreach (byte[] datagram in HostileDatagrams(connectRequest))
            {
                attacker.Send(datagram, datagram.Length, server);
                sent++;
            }
        }

        // Long enough for the server to read every datagram and for the address's request budget to refill.
        Thread.Sleep(1000);
        Assert.That(sent, Is.GreaterThan(1000));
        Assert.That(inbound.TrackedPeers, Is.Zero, "no datagram made a connection");
        Assert.That(inbound.Malformed, Is.Zero, "nothing reached the decoder");

        using var honest = new SocketClient(content, "honest-after-flood", "Honest1");
        honest.EnterWorld(port);

        Assert.That(host.Services.GetRequiredService<SessionRegistry>().Sessions, Has.Count.EqualTo(1));
        Assert.That(honest.Connection.MalformedMessages + honest.Connection.UnexpectedMessages, Is.Zero);
        host.StopAsync().GetAwaiter().GetResult();
        IReadOnlyList<string> lines = logs.Lines;
        Assert.That(lines.Any(line => line.Contains("ConnectionRefused")), Is.True, "the refusals were audited");
        AssertNoSecretIn(lines);
    }
}
}
