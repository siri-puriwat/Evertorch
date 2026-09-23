using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class LogoutMessageTests
{
    private static readonly byte[] LogoutBytes = { 0x0E, 0x00, 0x78, 0x56, 0x34, 0x12 };

    private static readonly byte[] CompleteBytes = { 0x19, 0x80 };

    private static bool ReadLogout(byte[] bytes)
    {
        return Logout.TryRead(bytes, out _);
    }

    private static bool ReadComplete(byte[] bytes)
    {
        return LogoutComplete.TryRead(bytes, out _);
    }

    [Test]
    public void LogoutComplete_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[LogoutComplete.EncodedLength];

        int length = new LogoutComplete().Write(written);

        Assert.That(length, Is.EqualTo(2));
        Assert.That(written, Is.EqualTo(CompleteBytes));
        Assert.That(LogoutComplete.TryRead(CompleteBytes, out _), Is.True);
    }

    [Test]
    public void LogoutComplete_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(CompleteBytes, ReadComplete);
        WireMatrix.AssertRejectsTrailingData(CompleteBytes, ReadComplete);
        WireMatrix.AssertRejectsOtherOpcodes(CompleteBytes, ReadComplete);
    }

    [Test]
    public void Logout_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[Logout.EncodedLength];

        int length = new Logout(0x12345678).Write(written);
        bool isRead = Logout.TryRead(LogoutBytes, out Logout read);

        Assert.That(length, Is.EqualTo(6));
        Assert.That(written, Is.EqualTo(LogoutBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
    }

    [Test]
    public void Logout_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(LogoutBytes, ReadLogout);
        WireMatrix.AssertRejectsTrailingData(LogoutBytes, ReadLogout);
        WireMatrix.AssertRejectsOtherOpcodes(LogoutBytes, ReadLogout);
    }
}
}
