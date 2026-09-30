using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class WornWeaponChangedTests
{
    public static readonly WornWeaponChanged Golden = new(new EntityId(0x0123456789ABCDEF), "item.a");

    public static readonly byte[] GoldenBytes =
    {
        0x20, 0x80,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61
    };

    private static byte[] Encode(WornWeaponChanged message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        Assert.That(message.Write(bytes), Is.EqualTo(bytes.Length));
        return bytes;
    }

    [Test]
    public void TryRead_ForAnEntityOfZeroOrANonItemId_ReturnsFalse()
    {
        byte[] noEntity = Encode(new WornWeaponChanged(new EntityId(0), "item.a"));
        byte[] notAnItem = Encode(new WornWeaponChanged(new EntityId(1), "Item A"));

        Assert.That(WornWeaponChanged.TryRead(noEntity, out _), Is.False);
        Assert.That(WornWeaponChanged.TryRead(notAnItem, out _), Is.False);
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        Assert.That(WornWeaponChanged.TryRead(GoldenBytes, out WornWeaponChanged? read), Is.True);
        Assert.That((read!.Entity, read.WornWeapon), Is.EqualTo((Golden.Entity, Golden.WornWeapon)));
    }

    [Test]
    public void TryRead_ForNoWeapon_ReadsItEmpty()
    {
        byte[] bytes = Encode(new WornWeaponChanged(new EntityId(3), string.Empty));

        Assert.That(bytes, Has.Length.EqualTo(12));
        Assert.That(WornWeaponChanged.TryRead(bytes, out WornWeaponChanged? read), Is.True);
        Assert.That(read!.WornWeapon, Is.Empty, "unarmed");
    }

    [Test]
    public void TryRead_WhenTruncatedOrFollowedByData_ReturnsFalse()
    {
        for (int length = 0; length < GoldenBytes.Length; length++)
        {
            Assert.That(WornWeaponChanged.TryRead(GoldenBytes.AsSpan(0, length), out _), Is.False, $"{length}");
        }

        byte[] longer = new byte[GoldenBytes.Length + 1];
        GoldenBytes.CopyTo(longer, 0);
        Assert.That(WornWeaponChanged.TryRead(longer, out _), Is.False);
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        Assert.That(Encode(Golden), Is.EqualTo(GoldenBytes));
    }
}
}
