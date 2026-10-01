using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ChatMessageTests
{
    private const int SendChannelOffset = 2;
    private const int ReceivedChannelOffset = 2;
    private const int ReceivedSpeakerOffset = 3;

    // A whisper of "hi" to Bobby with sequence 7.
    private static readonly byte[] SendBytes =
    {
        0x0A, 0x00,
        0x03,
        0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79,
        0x02, 0x00, 0x68, 0x69,
        0x07, 0x00, 0x00, 0x00
    };

    // "hi" said nearby by Anna, entity 42.
    private static readonly byte[] ReceivedBytes =
    {
        0x11, 0x80,
        0x01,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61,
        0x02, 0x00, 0x68, 0x69
    };

    public static ChatSend SendGolden => new(ChatChannel.Whisper, "Bobby", "hi", 7);

    public static ChatReceived ReceivedGolden => new(ChatChannel.Nearby, new EntityId(42), "Anna", "hi");

    private static byte[] Encode(ChatSend message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        message.Write(bytes);
        return bytes;
    }

    private static byte[] Encode(ChatReceived message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        message.Write(bytes);
        return bytes;
    }

    [TestCase((byte)0)]
    [TestCase((byte)5)]
    [TestCase((byte)255)]
    public void ChatReceived_WithAnUnknownChannel_IsMalformed(byte channel)
    {
        Assert.That(ChatReceived.TryRead(WireMatrix.With(ReceivedBytes, ReceivedChannelOffset, channel), out _),
            Is.False);
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("     ")]
    [TestCase("tab\there")]
    [TestCase("line\nbreak")]
    [TestCase("del\u007F")]
    [TestCase("caf\u00E9")]
    [TestCase("\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35")]
    public void ChatSend_WithTextThatBreaksTheRule_IsMalformed(string text)
    {
        Assert.That(ChatSend.TryRead(Encode(new ChatSend(ChatChannel.Nearby, "", text, 1)), out _), Is.False);
    }

    [TestCase((byte)0)]
    [TestCase((byte)4)]
    [TestCase((byte)255)]
    public void ChatSend_OnAChannelAClientMayNotSend_IsMalformed(byte channel)
    {
        Assert.That(ChatSend.TryRead(WireMatrix.With(SendBytes, SendChannelOffset, channel), out _), Is.False);
    }

    [Test]
    public void ChatReceived_ForEveryChannel_RoundTrips_AndTheSpeakerOnlyForNearby()
    {
        var party = new ChatReceived(ChatChannel.Party, default, "Anna", "hello all");
        var received = new ChatReceived(ChatChannel.Whisper, default, "Anna", "psst");
        var sent = new ChatReceived(ChatChannel.WhisperSent, default, "Bobby", "psst");
        var partyWithSpeaker = new ChatReceived(ChatChannel.Party, new EntityId(42), "Anna", "hi");
        var nearbyWithoutSpeaker = new ChatReceived(ChatChannel.Nearby, default, "Anna", "hi");

        Assert.That(ChatReceived.TryRead(Encode(party), out ChatReceived? readParty), Is.True);
        Assert.That(readParty!.Channel, Is.EqualTo(ChatChannel.Party));
        Assert.That(ChatReceived.TryRead(Encode(received), out _), Is.True);
        Assert.That(ChatReceived.TryRead(Encode(sent), out ChatReceived? readSent), Is.True);
        Assert.That((readSent!.Name, readSent.Text), Is.EqualTo(("Bobby", "psst")));
        Assert.That(ChatReceived.TryRead(Encode(partyWithSpeaker), out _), Is.False, "a speaker for nearby only");
        Assert.That(ChatReceived.TryRead(Encode(nearbyWithoutSpeaker), out _), Is.False, "nearby names its speaker");
        Assert.That(
            ChatReceived.TryRead(WireMatrix.With(ReceivedBytes, ReceivedSpeakerOffset, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF), out _),
            Is.False,
            "a speaker above 0");
    }

    [Test]
    public void ChatReceived_Largest_IsOneHundredEightyEightBytes()
    {
        var largest = new ChatReceived(
            ChatChannel.Nearby,
            new EntityId(long.MaxValue),
            new string('A', CharacterNames.MaxLength),
            new string('~', ChatText.MaxLength));

        byte[] bytes = Encode(largest);

        Assert.That(bytes, Has.Length.EqualTo(188));
        Assert.That(ChatReceived.TryRead(bytes, out _), Is.True);
    }

    [Test]
    public void ChatReceived_WhenCutShortOrTrailedOrMisnamed_IsMalformed()
    {
        WireMatrix.AssertRejectsEveryTruncation(ReceivedBytes, bytes => ChatReceived.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(ReceivedBytes, bytes => ChatReceived.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(ReceivedBytes, bytes => ChatReceived.TryRead(bytes, out _));
    }

    [Test]
    public void ChatReceived_WithAnInvalidNameOrText_IsMalformed()
    {
        Assert.That(
            ChatReceived.TryRead(Encode(new ChatReceived(ChatChannel.Party, default, "Ann", "hi")), out _),
            Is.False,
            "the name rule");
        Assert.That(
            ChatReceived.TryRead(Encode(new ChatReceived(ChatChannel.Party, default, "Anna", "<b>\u0001</b>")), out _),
            Is.False,
            "the text rule");
    }

    [Test]
    public void ChatReceived_WriteAndRead_MatchGoldenBytes()
    {
        Assert.That(Encode(ReceivedGolden), Is.EqualTo(ReceivedBytes));
        Assert.That(ChatReceived.TryRead(ReceivedBytes, out ChatReceived? read), Is.True);
        Assert.That(
            (read!.Channel, read.Speaker, read.Name, read.Text),
            Is.EqualTo((ChatChannel.Nearby, new EntityId(42), "Anna", "hi")));
    }

    [Test]
    public void ChatSend_Largest_IsOneHundredEightyFourBytes()
    {
        var largest = new ChatSend(
            ChatChannel.Whisper,
            new string('A', CharacterNames.MaxLength),
            new string('~', ChatText.MaxLength),
            uint.MaxValue);

        byte[] bytes = Encode(largest);

        Assert.That(bytes, Has.Length.EqualTo(184));
        Assert.That(bytes.Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxClientPayloadBytes));
        Assert.That(ChatSend.TryRead(bytes, out _), Is.True);
    }

    [Test]
    public void ChatSend_NearbyAndParty_CarryNoRecipient_AndAWhisperNeedsAValidOne()
    {
        Assert.That(ChatSend.TryRead(Encode(new ChatSend(ChatChannel.Nearby, "", "hi", 1)), out _), Is.True);
        Assert.That(ChatSend.TryRead(Encode(new ChatSend(ChatChannel.Party, "", "hi", 1)), out _), Is.True);
        Assert.That(ChatSend.TryRead(Encode(new ChatSend(ChatChannel.Nearby, "Bobby", "hi", 1)), out _), Is.False);
        Assert.That(ChatSend.TryRead(Encode(new ChatSend(ChatChannel.Whisper, "", "hi", 1)), out _), Is.False);
        Assert.That(ChatSend.TryRead(Encode(new ChatSend(ChatChannel.Whisper, "Bo b", "hi", 1)), out _), Is.False);
    }

    [Test]
    public void ChatSend_WhenCutShortOrTrailedOrMisnamed_IsMalformed()
    {
        WireMatrix.AssertRejectsEveryTruncation(SendBytes, bytes => ChatSend.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(SendBytes, bytes => ChatSend.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(SendBytes, bytes => ChatSend.TryRead(bytes, out _));
    }

    [Test]
    public void ChatSend_WithTextLongerThanTheRule_CannotBeWritten()
    {
        Action write = () => Encode(new ChatSend(ChatChannel.Nearby, "", new string('a', ChatText.MaxLength + 1), 1));

        Assert.That(write, Throws.ArgumentException);
    }

    [Test]
    public void ChatSend_WriteAndRead_MatchGoldenBytes()
    {
        Assert.That(Encode(SendGolden), Is.EqualTo(SendBytes));
        Assert.That(ChatSend.TryRead(SendBytes, out ChatSend? read), Is.True);
        Assert.That(
            (read!.Channel, read.Recipient, read.Text, read.CommandSequence),
            Is.EqualTo((ChatChannel.Whisper, "Bobby", "hi", 7u)));
    }
}
}
