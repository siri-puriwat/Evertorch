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

    private static IHost StartHost(TemporaryDirectory root, CapturingLoggerProvider logs)
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
            },
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
        // carries.
        int key = IndexOf(connectRequest, Encoding.UTF8.GetBytes(Key));
        foreach (int size in new[] { 1000, 20000, 60000 })
        {
            byte[] oversized = new byte[key + size];
            connectRequest.AsSpan(0, key).CopyTo(oversized);
            random.NextBytes(oversized.AsSpan(key));
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
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
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
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        uint content = host.Services.GetRequiredService<HandshakeValidator>().RequiredClientContentVersion;

        using TestNetClient signedIn = Connected(port);
        signedIn.Send(
            Hello(ProtocolConstants.ProtocolVersion, content, "dev:secret-login-one"),
            ProtocolChannel.Control,
            DeliveryMethod.ReliableOrdered);
        Assert.That(
            signedIn.WaitFor(() => signedIn.Received.Any(message =>
                MessageRouting.TryReadOpcode(message.Payload, out MessageOpcode opcode)
                && opcode == MessageOpcode.ServerHello)),
            Is.True,
            "signed in");
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

        Assert.That(signedIn.WaitFor(() => signedIn.IsDisconnected), Is.True);
        Assert.That(mismatched.WaitFor(() => mismatched.IsDisconnected), Is.True);
        Assert.That(forged.WaitFor(() => forged.IsDisconnected), Is.True);
        host.StopAsync().GetAwaiter().GetResult();

        IReadOnlyList<string> lines = logs.Lines;
        Assert.That(signedIn.Notice!.Reason, Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(lines.Any(line => line.Contains("ViolationDisconnect")), Is.True, "the audit events were written");
        Assert.That(lines.Where(line => line.Contains("secret", StringComparison.OrdinalIgnoreCase)), Is.Empty);
        Assert.That(lines.Where(line => line.Contains("Password", StringComparison.OrdinalIgnoreCase)), Is.Empty);
        Assert.That(lines.Where(line => line.Contains(TestHosts.UnreachableDatabase)), Is.Empty);
    }

    [Test]
    public void RawDatagrams_OfGarbageTruncatedHeadersAndOversizedRequests_LeaveTheServerServingHonestClients()
    {
        using var root = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();
        using IHost host = StartHost(root, logs);
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
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
    }
}
}
