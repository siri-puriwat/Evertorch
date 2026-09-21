using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ContentVersionCodecTests
{
    [TestCase("11326bd1bdfe0c49", 0x11326BD1u)]
    [TestCase("00000000ffffffff", 0u)]
    [TestCase("ffffffff00000000", 0xFFFFFFFFu)]
    [TestCase("0123456789abcdef", 0x01234567u)]
    public void TryToWire_ForManifestVersion_UsesTheFirstEightDigits(string version, uint expected)
    {
        bool isConverted = ContentVersionCodec.TryToWire(version, out uint wire);

        Assert.That(isConverted, Is.True);
        Assert.That(wire, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("11326bd1")]
    [TestCase("11326bd1bdfe0c490")]
    [TestCase("11326BD1BDFE0C49")]
    [TestCase("11326bd1bdfe0c4g")]
    [TestCase("g1326bd1bdfe0c49")]
    [TestCase("11326bd1 dfe0c49")]
    public void TryToWire_ForMalformedVersion_ReturnsFalse(string? version)
    {
        bool isConverted = ContentVersionCodec.TryToWire(version, out uint wire);

        Assert.That(isConverted, Is.False);
        Assert.That(wire, Is.EqualTo(0u));
    }
}
}
