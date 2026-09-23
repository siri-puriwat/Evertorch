using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the character selection messages, so both compilers and runtimes agree on the
///     wire format.
/// </summary>
[TestFixture]
public sealed class SharedCharacterMessageTests
{
    private static readonly byte[] CreateBytes = { 0x0D, 0x00, 0x04, 0x00, 0x41, 0x6E, 0x6E, 0x30 };

    private static readonly byte[] ResultBytes =
    {
        0x17, 0x80, 0x01, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01
    };

    private static byte[] ListBytes()
    {
        var bytes = new List<byte> { 0x16, 0x80, 0x02 };
        bytes.AddRange(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0x04, 0x00 });
        bytes.AddRange(Encoding.ASCII.GetBytes("Ann0"));
        bytes.AddRange(new byte[] { 0x0E, 0x00 });
        bytes.AddRange(Encoding.ASCII.GetBytes("job.adventurer"));
        bytes.AddRange(new byte[] { 0x01, 0x00 });
        bytes.AddRange(new byte[] { 0x08, 0, 0, 0, 0, 0, 0, 0, 0x05, 0x00 });
        bytes.AddRange(Encoding.ASCII.GetBytes("Bob12"));
        bytes.AddRange(new byte[] { 0x0E, 0x00 });
        bytes.AddRange(Encoding.ASCII.GetBytes("job.adventurer"));
        bytes.AddRange(new byte[] { 0x0C, 0x00 });
        return bytes.ToArray();
    }

    [Test]
    public void CharacterList_WriteAndRead_MatchGoldenBytes()
    {
        var job = new JobDefinitionId("job.adventurer");
        var list = new CharacterList(
            new[]
            {
                new CharacterListEntry(new CharacterId(7), "Ann0", job, 1),
                new CharacterListEntry(new CharacterId(8), "Bob12", job, 12)
            });
        byte[] buffer = new byte[list.GetEncodedLength()];
        list.Write(buffer);

        bool isRead = CharacterList.TryRead(ListBytes(), out CharacterList? read);

        Assert.That(buffer, Is.EqualTo(ListBytes()));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Characters[1].Name, Is.EqualTo("Bob12"));
        Assert.That(read.Characters[1].BaseLevel, Is.EqualTo(12));
    }

    [Test]
    public void CreateCharacterResult_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[CreateCharacterResult.EncodedLength];
        new CreateCharacterResult(CreateCharacterOutcome.Created, new CharacterId(0x0123456789ABCDEF)).Write(buffer);

        bool isRead = CreateCharacterResult.TryRead(ResultBytes, out CreateCharacterResult read);

        Assert.That(buffer, Is.EqualTo(ResultBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Character, Is.EqualTo(new CharacterId(0x0123456789ABCDEF)));
    }

    [Test]
    public void CreateCharacter_WriteAndRead_MatchGoldenBytes()
    {
        var message = new CreateCharacter("Ann0");
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = CreateCharacter.TryRead(CreateBytes, out CreateCharacter? read);

        Assert.That(buffer, Is.EqualTo(CreateBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Name, Is.EqualTo("Ann0"));
    }

    [Test]
    public void LogoutAndLogoutComplete_WriteAndRead_MatchGoldenBytes()
    {
        byte[] logout = new byte[Logout.EncodedLength];
        new Logout(0x12345678).Write(logout);
        byte[] complete = new byte[LogoutComplete.EncodedLength];
        new LogoutComplete().Write(complete);

        Assert.That(logout, Is.EqualTo(new byte[] { 0x0E, 0x00, 0x78, 0x56, 0x34, 0x12 }));
        Assert.That(Logout.TryRead(logout, out Logout read), Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
        Assert.That(complete, Is.EqualTo(new byte[] { 0x19, 0x80 }));
        Assert.That(LogoutComplete.TryRead(complete, out LogoutComplete _), Is.True);
    }

    [Test]
    public void MessageRouting_ForCharacterMessages_UsesTheControlChannel()
    {
        foreach (MessageOpcode opcode in new[]
                 {
                     MessageOpcode.CreateCharacter, MessageOpcode.CharacterList, MessageOpcode.CreateCharacterResult,
                     MessageOpcode.Logout, MessageOpcode.LogoutComplete
                 })
        {
            Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery));
            Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
            Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        }
    }
}
}
