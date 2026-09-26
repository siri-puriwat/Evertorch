using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the character messages (selection, logout, and progress), so both compilers and
///     runtimes agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedCharacterMessageTests
{
    private static readonly byte[] CreateBytes = { 0x0D, 0x00, 0x04, 0x00, 0x41, 0x6E, 0x6E, 0x30 };

    private static readonly byte[] ResultBytes =
    {
        0x17, 0x80, 0x01, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01
    };

    private static readonly byte[] ListBytes = BuildList();

    private static readonly byte[] LogoutBytes = { 0x0E, 0x00, 0x78, 0x56, 0x34, 0x12 };

    private static readonly byte[] LogoutCompleteBytes = { 0x19, 0x80 };

    private static readonly byte[] ProgressBytes =
    {
        0x1A, 0x80,
        0x02, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11
    };

    private static byte[] BuildList()
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

        bool isRead = CharacterList.TryRead(ListBytes, out CharacterList? read);

        Assert.That(buffer, Is.EqualTo(ListBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Characters[1].Name, Is.EqualTo("Bob12"));
        Assert.That(read.Characters[1].BaseLevel, Is.EqualTo(12));
    }

    [Test]
    public void CharacterProgress_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[CharacterProgress.EncodedLength];
        new CharacterProgress(2, 0x0102030405060708UL, 0x1112131415161718UL).Write(buffer);

        bool isRead = CharacterProgress.TryRead(ProgressBytes, out CharacterProgress read);

        Assert.That(buffer, Is.EqualTo(ProgressBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Level, Is.EqualTo(2));
        Assert.That(read.Experience, Is.EqualTo(0x0102030405060708UL));
        Assert.That(read.ExperienceToNextLevel, Is.EqualTo(0x1112131415161718UL));
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

        Assert.That(logout, Is.EqualTo(LogoutBytes));
        Assert.That(Logout.TryRead(LogoutBytes, out Logout read), Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
        Assert.That(complete, Is.EqualTo(LogoutCompleteBytes));
        Assert.That(LogoutComplete.TryRead(LogoutCompleteBytes, out LogoutComplete _), Is.True);
    }

    [Test]
    public void MessageRouting_ForCharacterMessages_UsesTheControlChannel()
    {
        foreach (MessageOpcode opcode in new[]
                 {
                     MessageOpcode.CreateCharacter, MessageOpcode.CharacterList, MessageOpcode.CreateCharacterResult,
                     MessageOpcode.Logout, MessageOpcode.LogoutComplete, MessageOpcode.CharacterProgress
                 })
        {
            Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery));
            Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
            Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        }
    }
}
}
