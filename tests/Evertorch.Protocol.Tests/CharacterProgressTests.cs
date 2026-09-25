using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class CharacterProgressTests
{
    private static readonly byte[] GoldenBytes =
    {
        0x1A, 0x80,
        0x02, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11
    };

    [Test]
    public void TryRead_AtTheLevelCap_AcceptsNoExperienceToTheNextLevel()
    {
        byte[] atCap = WireMatrix.With(GoldenBytes, 12, 0, 0, 0, 0, 0, 0, 0, 0);

        bool isRead = CharacterProgress.TryRead(atCap, out CharacterProgress message);

        Assert.That(isRead, Is.True);
        Assert.That(message.ExperienceToNextLevel, Is.Zero);
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = CharacterProgress.TryRead(GoldenBytes, out CharacterProgress message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Level, Is.EqualTo(2));
        Assert.That(message.Experience, Is.EqualTo(0x0102030405060708UL));
        Assert.That(message.ExperienceToNextLevel, Is.EqualTo(0x1112131415161718UL));
    }

    [Test]
    public void TryRead_WhenLevelIsZero_ReturnsFalse()
    {
        Assert.That(CharacterProgress.TryRead(WireMatrix.With(GoldenBytes, 2, 0, 0), out _), Is.False);
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => CharacterProgress.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => CharacterProgress.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => CharacterProgress.TryRead(bytes, out _));
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[CharacterProgress.EncodedLength];

        int written = new CharacterProgress(2, 0x0102030405060708UL, 0x1112131415161718UL).Write(buffer);

        Assert.That(written, Is.EqualTo(20));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }
}
}
