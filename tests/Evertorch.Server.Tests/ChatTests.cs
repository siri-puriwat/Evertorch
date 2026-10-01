using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Chat on the server (Milestone 12 line 4; Gameplay Systems §15; Network Protocol §9, §11): nearby reaches the
///     speaker and those on its map that know it, a whisper reaches its recipient with the speaker's echo, party chat
///     without a party is refused (party chat itself is in PartySyncTests), and chat is applied while dead and refused
///     while logging out.
/// </summary>
[TestFixture]
public sealed class ChatTests
{
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");

    private static ChatReceived[] Lines(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.ChatReceived)
            .Select(message => ChatReceived.TryRead(message.Payload, out ChatReceived? line) ? line! : null!)
            .ToArray();
    }

    private static CommandRejected[] Rejections(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    private static MapInstance MapOf(TestServer server, MapDefinitionId id)
    {
        server.World.TryGetMap(id, out MapInstance? map);
        return map!;
    }

    // Through the training ground's portal onto the field.
    private static void CrossToTheField(TestServer server, ConnectionId player)
    {
        server.PlayerOf(player).Position = MapOf(server, Ground).Definition.Portals.Single().Center;
        server.TickUntil(() => server.SessionOf(player).Character!.Map.Definition.Id == Field);
    }

    [Test]
    public void Chat_IsNeverWrittenToALog()
    {
        var server = new TestServer(abuseOptions: new AbuseOptions());
        ConnectionId speaker = server.EnterWorld(7);
        server.EnterWorld(8);

        server.SendChat(speaker, ChatChannel.Nearby, string.Empty, "SecretWordNearby", 1);
        server.SendChat(speaker, ChatChannel.Whisper, "Tester8", "SecretWordWhisper", 2);
        server.SendChat(speaker, ChatChannel.Whisper, "Nobody1", "SecretWordRefused", 3);
        server.SendChat(speaker, ChatChannel.Party, string.Empty, "SecretWordParty", 4);
        server.Tick();

        string logged = string.Join("\n", server.AllLogText());
        Assert.That(logged, Does.Not.Contain("SecretWord"));
    }

    [Test]
    public void Chat_PastItsBurst_IsThrottledWithThree_AndScored()
    {
        var server = new TestServer(abuseOptions: new AbuseOptions());
        ConnectionId speaker = server.EnterWorld(7);
        int burst = new AbuseOptions().ChatCommandBurst;

        for (uint line = 1; line <= burst + 1; line++)
        {
            server.SendChat(speaker, ChatChannel.Nearby, string.Empty, $"line {line}", line);
        }

        server.Tick();

        Assert.That(Lines(server, speaker), Has.Length.EqualTo(burst));
        Assert.That(
            Rejections(server, speaker).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[] { ((uint)burst + 1, CommandRejectionReason.NotAllowedNow) }));
        Assert.That(server.SessionOf(speaker).ThrottledCommands, Is.EqualTo(1));
    }

    [Test]
    public void Chat_WhileDead_IsApplied()
    {
        var server = new TestServer();
        ConnectionId speaker = server.EnterWorld(7);
        ConnectionId near = server.EnterWorld(8);
        server.Combat.Kill(MapOf(server, Ground), server.PlayerOf(speaker), null, server.CurrentTick);
        server.Tick();

        server.SendChat(speaker, ChatChannel.Nearby, string.Empty, "help", 1);
        server.SendChat(speaker, ChatChannel.Whisper, "Tester8", "help me", 2);
        server.Tick();

        Assert.That(server.PlayerOf(speaker).IsDead, Is.True);
        Assert.That(Rejections(server, speaker), Is.Empty);
        Assert.That(Lines(server, near).Select(line => line.Text), Is.EqualTo(new[] { "help", "help me" }));
    }

    [Test]
    public void Chat_WhileLoggingOut_IsRefusedWithThree()
    {
        var server = new TestServer();
        ConnectionId speaker = server.EnterWorld(7);
        ConnectionId near = server.EnterWorld(8);

        server.SendLogout(speaker, 1);
        server.SendChat(speaker, ChatChannel.Nearby, string.Empty, "bye", 2);
        server.Tick();

        Assert.That(
            Rejections(server, speaker).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[] { (2u, CommandRejectionReason.NotAllowedNow) }));
        Assert.That(Lines(server, near), Is.Empty);
    }

    [Test]
    public void Nearby_ReachesTheSpeakerAndThoseOnItsMapThatSeeIt_AndNoOneElsewhere()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId speaker = server.EnterWorld(7);
        ConnectionId near = server.EnterWorld(8);
        ConnectionId away = server.EnterWorld(9);
        CrossToTheField(server, away);
        server.Tick(2);

        server.SendChat(speaker, ChatChannel.Nearby, string.Empty, "hello there", 1);
        server.Tick();

        (ChatChannel, EntityId, string, string) expected =
            (ChatChannel.Nearby, server.PlayerOf(speaker).Id, "Tester7", "hello there");
        Assert.That(Lines(server, speaker).Select(line => (line.Channel, line.Speaker, line.Name, line.Text)),
            Is.EqualTo(new[] { expected }), "the speaker hears itself");
        Assert.That(Lines(server, near).Select(line => (line.Channel, line.Speaker, line.Name, line.Text)),
            Is.EqualTo(new[] { expected }));
        Assert.That(Lines(server, away), Is.Empty, "another map");
        Assert.That(Rejections(server, speaker), Is.Empty);
    }

    [Test]
    public void Party_WithoutAParty_IsRefusedWithThree()
    {
        var server = new TestServer();
        ConnectionId speaker = server.EnterWorld(7);
        ConnectionId near = server.EnterWorld(8);

        server.SendChat(speaker, ChatChannel.Party, string.Empty, "anyone?", 1);
        server.Tick();

        Assert.That(
            Rejections(server, speaker).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[] { (1u, CommandRejectionReason.NotAllowedNow) }));
        Assert.That(Lines(server, speaker).Concat(Lines(server, near)), Is.Empty);
    }

    [Test]
    public void Whisper_ReachesItsRecipient_AndEchoesToTheSpeakerAsSent_WhateverTheCaseOrMap()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId speaker = server.EnterWorld(7);
        ConnectionId recipient = server.EnterWorld(8);
        ConnectionId bystander = server.EnterWorld(9);
        CrossToTheField(server, recipient);

        server.SendChat(speaker, ChatChannel.Whisper, "tESTER8", "psst", 1);
        server.Tick();

        Assert.That(Lines(server, recipient).Select(line => (line.Channel, line.Speaker, line.Name, line.Text)),
            Is.EqualTo(new[] { (ChatChannel.Whisper, default(EntityId), "Tester7", "psst") }));
        Assert.That(Lines(server, speaker).Select(line => (line.Channel, line.Speaker, line.Name, line.Text)),
            Is.EqualTo(new[] { (ChatChannel.WhisperSent, default(EntityId), "Tester8", "psst") }),
            "the recipient's name as stored");
        Assert.That(Lines(server, bystander), Is.Empty);
        Assert.That(Rejections(server, speaker), Is.Empty);
    }

    [Test]
    public void Whisper_ToNoOneOrToItself_OrToOneInItsReconnectGrace_IsRefusedWithOne()
    {
        var server = new TestServer(reconnectGraceMs: 30_000);
        ConnectionId speaker = server.EnterWorld(7);
        ConnectionId gone = server.EnterWorld(8);
        server.Disconnect(gone);
        server.Tick();

        server.SendChat(speaker, ChatChannel.Whisper, "Nobody1", "hi", 1);
        server.SendChat(speaker, ChatChannel.Whisper, "Tester7", "hi", 2);
        server.SendChat(speaker, ChatChannel.Whisper, "Tester8", "hi", 3);
        server.Tick();

        Assert.That(
            Rejections(server, speaker).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[]
            {
                (1u, CommandRejectionReason.InvalidTarget), (2u, CommandRejectionReason.InvalidTarget),
                (3u, CommandRejectionReason.InvalidTarget)
            }));
        Assert.That(Lines(server, speaker), Is.Empty, "nothing echoed");
    }
}
}
