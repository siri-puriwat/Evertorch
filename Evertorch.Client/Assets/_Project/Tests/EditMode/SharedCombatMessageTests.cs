using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the combat messages so both compilers and runtimes agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedCombatMessageTests
{
    private static readonly byte[] AttackBytes =
    {
        0x06, 0x00,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x78, 0x56, 0x34, 0x12,
    };

    private static readonly byte[] CancelBytes = { 0x07, 0x00, 0x78, 0x56, 0x34, 0x12, };

    private static readonly byte[] RespawnBytes = { 0x0C, 0x00, 0x78, 0x56, 0x34, 0x12, };

    [Test]
    public void AttackEntity_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[AttackEntity.EncodedLength];
        new AttackEntity(new EntityId(0x0123456789ABCDEF), 0x12345678).Write(buffer);

        bool isRead = AttackEntity.TryRead(AttackBytes, out AttackEntity read);

        Assert.That(buffer, Is.EqualTo(AttackBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
    }

    [Test]
    public void CancelAndRespawn_WriteAndRead_MatchGoldenBytes()
    {
        byte[] cancel = new byte[CancelAction.EncodedLength];
        byte[] respawn = new byte[Respawn.EncodedLength];
        new CancelAction(0x12345678).Write(cancel);
        new Respawn(0x12345678).Write(respawn);

        Assert.That(cancel, Is.EqualTo(CancelBytes));
        Assert.That(respawn, Is.EqualTo(RespawnBytes));
        Assert.That(CancelAction.TryRead(CancelBytes, out CancelAction _), Is.True);
        Assert.That(Respawn.TryRead(RespawnBytes, out Respawn _), Is.True);
    }
}
}
