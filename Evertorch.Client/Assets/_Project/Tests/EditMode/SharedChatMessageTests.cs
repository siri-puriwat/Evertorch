using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedChatMessageTests
{
    // A whisper of "hi" to Bobby with sequence 7.
    private static readonly byte[] ChatSendBytes =
    {
        0x0A, 0x00,
        0x03,
        0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79,
        0x02, 0x00, 0x68, 0x69,
        0x07, 0x00, 0x00, 0x00
    };

    // "hi" said nearby by Anna, entity 42.
    private static readonly byte[] ChatReceivedBytes =
    {
        0x11, 0x80,
        0x01,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61,
        0x02, 0x00, 0x68, 0x69
    };

    [Test]
    public void ChatReceived_WriteAndRead_MatchGoldenBytes()
    {
        var message = new ChatReceived(ChatChannel.Nearby, new EntityId(42), "Anna", "hi");
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = ChatReceived.TryRead(ChatReceivedBytes, out ChatReceived? read);

        Assert.That(buffer, Is.EqualTo(ChatReceivedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Name, Is.EqualTo("Anna"));
        Assert.That(read.Text, Is.EqualTo("hi"));
    }

    [Test]
    public void ChatSend_WriteAndRead_MatchGoldenBytes()
    {
        var message = new ChatSend(ChatChannel.Whisper, "Bobby", "hi", 7);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = ChatSend.TryRead(ChatSendBytes, out ChatSend? read);

        Assert.That(buffer, Is.EqualTo(ChatSendBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Recipient, Is.EqualTo("Bobby"));
        Assert.That(read.CommandSequence, Is.EqualTo(7u));
    }

    [Test]
    public void ChatText_TakesPrintableAsciiOnly_AsOnTheServer()
    {
        Assert.That(ChatText.IsValid("hi <b>"), Is.True);
        Assert.That(ChatText.IsValid("   "), Is.False);
        Assert.That(ChatText.IsValid("caf\u00E9"), Is.False);
        Assert.That(ChatText.IsValid(new string('a', ChatText.MaxLength + 1)), Is.False);
    }
}
}
