using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Character selection and creation (Network Protocol §4, §6).
/// </summary>
[TestFixture]
public sealed class CharacterMessageTests
{
    private static readonly byte[] CreateBytes = { 0x0D, 0x00, 0x04, 0x00, 0x41, 0x6E, 0x6E, 0x30 };

    private static readonly byte[] ResultBytes =
    {
        0x17, 0x80, 0x01, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01
    };

    private static readonly byte[] ListBytes = BuildList();

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

    private static CharacterList SampleList()
    {
        var job = new JobDefinitionId("job.adventurer");
        return new CharacterList(
            new[]
            {
                new CharacterListEntry(new CharacterId(7), "Ann0", job, 1),
                new CharacterListEntry(new CharacterId(8), "Bob12", job, 12)
            });
    }

    private static bool ReadCreate(byte[] bytes)
    {
        return CreateCharacter.TryRead(bytes, out _);
    }

    private static bool ReadResult(byte[] bytes)
    {
        return CreateCharacterResult.TryRead(bytes, out _);
    }

    private static bool ReadList(byte[] bytes)
    {
        return CharacterList.TryRead(bytes, out _);
    }

    [TestCase((byte)0, 0L)]
    [TestCase((byte)6, 0L)]
    [TestCase((byte)1, 0L)]
    [TestCase((byte)1, -3L)]
    [TestCase((byte)2, 7L)]
    [TestCase((byte)5, 7L)]
    public void CreateCharacterResult_WithAnUnknownOutcomeOrAMismatchedCharacter_IsRefused(byte outcome, long character)
    {
        byte[] bytes = new byte[CreateCharacterResult.EncodedLength];
        bytes[0] = 0x17;
        bytes[1] = 0x80;
        bytes[2] = outcome;
        BitConverter.GetBytes(character).CopyTo(bytes, 3);

        Assert.That(CreateCharacterResult.TryRead(bytes, out _), Is.False);
    }

    [TestCase(CreateCharacterOutcome.NameInvalid)]
    [TestCase(CreateCharacterOutcome.NameTaken)]
    [TestCase(CreateCharacterOutcome.LimitReached)]
    [TestCase(CreateCharacterOutcome.ServiceUnavailable)]
    public void CreateCharacterResult_ForARefusal_CarriesNoCharacter(CreateCharacterOutcome outcome)
    {
        byte[] bytes = new byte[CreateCharacterResult.EncodedLength];
        new CreateCharacterResult(outcome, default).Write(bytes);

        Assert.That(CreateCharacterResult.TryRead(bytes, out CreateCharacterResult read), Is.True);
        Assert.That(read.Outcome, Is.EqualTo(outcome));
        Assert.That(read.Character, Is.EqualTo(default(CharacterId)));
    }

    [TestCase(3, (byte)0x00)]
    [TestCase(33, (byte)0x00)]
    [TestCase(19, (byte)0x2E)]
    public void CharacterList_WithAZeroIdAZeroLevelOrABadJob_IsRefused(int offset, byte value)
    {
        byte[] bytes = WireMatrix.With(ListBytes, offset, value);

        Assert.That(CharacterList.TryRead(bytes, out _), Is.False);
    }

    [Test]
    public void CharacterList_AtItsLargest_FitsOneDatagram()
    {
        string name = new('A', 23);
        string job = $"job.{new string('a', 60)}";
        var entry = new CharacterListEntry(new CharacterId(1), name, new JobDefinitionId(job), ushort.MaxValue);

        int length = new CharacterList(new[] { entry, entry, entry }).GetEncodedLength();

        Assert.That(length, Is.EqualTo(3 + 3 * (8 + 25 + 66 + 2)));
    }

    [Test]
    public void CharacterList_Empty_IsThreeBytes()
    {
        var empty = new CharacterList(Array.Empty<CharacterListEntry>());
        byte[] written = new byte[empty.GetEncodedLength()];

        empty.Write(written);

        Assert.That(written, Is.EqualTo(new byte[] { 0x16, 0x80, 0x00 }));
        Assert.That(CharacterList.TryRead(written, out CharacterList? read), Is.True);
        Assert.That(read!.Characters, Is.Empty);
    }

    [Test]
    public void CharacterList_ForGoldenBytes_RoundTrips()
    {
        CharacterList message = SampleList();
        byte[] written = new byte[message.GetEncodedLength()];

        int length = message.Write(written);
        bool isRead = CharacterList.TryRead(ListBytes, out CharacterList? read);

        Assert.That(length, Is.EqualTo(68));
        Assert.That(written, Is.EqualTo(ListBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Characters, Has.Count.EqualTo(2));
        Assert.That(read.Characters[1].Character, Is.EqualTo(new CharacterId(8)));
        Assert.That(read.Characters[1].Name, Is.EqualTo("Bob12"));
        Assert.That(read.Characters[1].Job.Value, Is.EqualTo("job.adventurer"));
        Assert.That(read.Characters[1].BaseLevel, Is.EqualTo(12));
    }

    [Test]
    public void CharacterList_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(ListBytes, ReadList);
        WireMatrix.AssertRejectsTrailingData(ListBytes, ReadList);
        WireMatrix.AssertRejectsOtherOpcodes(ListBytes, ReadList);
    }

    [Test]
    public void CharacterList_WithMoreThanThreeEntries_IsRefusedBeforeReadingThem()
    {
        byte[] bytes = { 0x16, 0x80, 0x04 };
        var entry = new CharacterListEntry(new CharacterId(1), "Abcd", new JobDefinitionId("job.adventurer"), 1);
        Action build = () => _ = new CharacterList(new[] { entry, entry, entry, entry });

        Assert.That(CharacterList.TryRead(bytes, out _), Is.False);
        Assert.That(build, Throws.ArgumentException);
    }

    [Test]
    public void CreateCharacterResult_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[CreateCharacterResult.EncodedLength];

        int length = new CreateCharacterResult(CreateCharacterOutcome.Created, new CharacterId(0x0123456789ABCDEF))
            .Write(written);
        bool isRead = CreateCharacterResult.TryRead(ResultBytes, out CreateCharacterResult read);

        Assert.That(length, Is.EqualTo(11));
        Assert.That(written, Is.EqualTo(ResultBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Outcome, Is.EqualTo(CreateCharacterOutcome.Created));
        Assert.That(read.Character, Is.EqualTo(new CharacterId(0x0123456789ABCDEF)));
    }

    [Test]
    public void CreateCharacterResult_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(ResultBytes, ReadResult);
        WireMatrix.AssertRejectsTrailingData(ResultBytes, ReadResult);
        WireMatrix.AssertRejectsOtherOpcodes(ResultBytes, ReadResult);
    }

    [Test]
    public void CreateCharacter_ForGoldenBytes_RoundTrips()
    {
        var message = new CreateCharacter("Ann0");
        byte[] written = new byte[message.GetEncodedLength()];

        int length = message.Write(written);
        bool isRead = CreateCharacter.TryRead(CreateBytes, out CreateCharacter? read);

        Assert.That(length, Is.EqualTo(8));
        Assert.That(written, Is.EqualTo(CreateBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Name, Is.EqualTo("Ann0"));
    }

    [Test]
    public void CreateCharacter_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(CreateBytes, ReadCreate);
        WireMatrix.AssertRejectsTrailingData(CreateBytes, ReadCreate);
        WireMatrix.AssertRejectsOtherOpcodes(CreateBytes, ReadCreate);
    }

    [Test]
    public void CreateCharacter_WithANameLongerThanTwentyThreeBytes_IsRefusedOnBothSides()
    {
        var bytes = new List<byte> { 0x0D, 0x00, 0x18, 0x00 };
        bytes.AddRange(Encoding.ASCII.GetBytes("Abcdefghijklmnopqrstuvwx"));
        var tooLong = new CreateCharacter("Abcdefghijklmnopqrstuvwx");
        Action write = () => tooLong.Write(new byte[64]);

        Assert.That(CreateCharacter.TryRead(bytes.ToArray(), out _), Is.False);
        Assert.That(write, Throws.ArgumentException);
    }
}
}
