using System;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class MessageRoutingTests
{
    private static MessageOpcode[] RealOpcodes => ((MessageOpcode[])Enum.GetValues(typeof(MessageOpcode)))
        .Where(opcode => opcode != MessageOpcode.None)
        .ToArray();

    [TestCaseSource(nameof(RealOpcodes))]
    public void TryGetRoute_ForEveryOpcode_HasARoute(MessageOpcode opcode)
    {
        Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel _, out MessageDelivery _), Is.True);
    }

    [TestCase(MessageOpcode.ClientHello)]
    [TestCase(MessageOpcode.EnterWorldRequest)]
    [TestCase(MessageOpcode.TargetEntity)]
    [TestCase(MessageOpcode.AttackEntity)]
    [TestCase(MessageOpcode.PickupItem)]
    [TestCase(MessageOpcode.CancelAction)]
    [TestCase(MessageOpcode.UseSkill)]
    [TestCase(MessageOpcode.Respawn)]
    [TestCase(MessageOpcode.CreateCharacter)]
    [TestCase(MessageOpcode.Logout)]
    [TestCase(MessageOpcode.InventoryResyncRequest)]
    [TestCase(MessageOpcode.ServerHello)]
    [TestCase(MessageOpcode.WorldEntered)]
    [TestCase(MessageOpcode.EntitySpawn)]
    [TestCase(MessageOpcode.EntityDespawn)]
    [TestCase(MessageOpcode.TargetChanged)]
    [TestCase(MessageOpcode.AttackStarted)]
    [TestCase(MessageOpcode.Damage)]
    [TestCase(MessageOpcode.EntityDied)]
    [TestCase(MessageOpcode.SkillCastStarted)]
    [TestCase(MessageOpcode.SkillResolved)]
    [TestCase(MessageOpcode.ItemDropped)]
    [TestCase(MessageOpcode.ItemPickedUp)]
    [TestCase(MessageOpcode.CharacterHealth)]
    [TestCase(MessageOpcode.CharacterProgress)]
    [TestCase(MessageOpcode.SkillList)]
    [TestCase(MessageOpcode.EntityRevived)]
    [TestCase(MessageOpcode.DisconnectNotice)]
    [TestCase(MessageOpcode.CharacterList)]
    [TestCase(MessageOpcode.CreateCharacterResult)]
    [TestCase(MessageOpcode.LogoutComplete)]
    [TestCase(MessageOpcode.CommandRejected)]
    [TestCase(MessageOpcode.InventorySnapshot)]
    [TestCase(MessageOpcode.InventoryChanged)]
    public void TryGetRoute_ForSessionAndLifecycleMessages_IsReliableOrderedOnControl(MessageOpcode opcode)
    {
        MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery);

        Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
    }

    [TestCase(MessageOpcode.MoveInput, ProtocolChannel.Input)]
    [TestCase(MessageOpcode.StopMovement, ProtocolChannel.Input)]
    [TestCase(MessageOpcode.EntitySnapshot, ProtocolChannel.State)]
    public void TryGetRoute_ForRealtimeMessages_IsUnreliableSequencedOnItsOwnChannel(
        MessageOpcode opcode,
        ProtocolChannel expectedChannel)
    {
        MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery);

        Assert.That(channel, Is.EqualTo(expectedChannel));
        Assert.That(delivery, Is.EqualTo(MessageDelivery.UnreliableSequenced));
    }

    [TestCase((ushort)0)]
    [TestCase((ushort)0x0040)]
    [TestCase((ushort)0x7FFF)]
    [TestCase((ushort)0xFFFF)]
    public void TryGetRoute_ForUnassignedOpcode_ReturnsFalse(ushort value)
    {
        Assert.That(
            MessageRouting.TryGetRoute((MessageOpcode)value, out ProtocolChannel _, out MessageDelivery _),
            Is.False);
    }

    [TestCase(MessageOpcode.ClientHello, true)]
    [TestCase(MessageOpcode.EnterWorldRequest, true)]
    [TestCase(MessageOpcode.TargetEntity, true)]
    [TestCase(MessageOpcode.AttackEntity, true)]
    [TestCase(MessageOpcode.CancelAction, true)]
    [TestCase(MessageOpcode.PickupItem, true)]
    [TestCase(MessageOpcode.Respawn, true)]
    [TestCase(MessageOpcode.MoveInput, true)]
    [TestCase(MessageOpcode.StopMovement, true)]
    [TestCase(MessageOpcode.EntitySnapshot, false)]
    [TestCase(MessageOpcode.None, false)]
    [TestCase(MessageOpcode.ServerHello, false)]
    [TestCase(MessageOpcode.WorldEntered, false)]
    [TestCase(MessageOpcode.EntitySpawn, false)]
    [TestCase(MessageOpcode.EntityDespawn, false)]
    [TestCase(MessageOpcode.TargetChanged, false)]
    [TestCase(MessageOpcode.AttackStarted, false)]
    [TestCase(MessageOpcode.EntityRevived, false)]
    [TestCase(MessageOpcode.ItemDropped, false)]
    [TestCase(MessageOpcode.ItemPickedUp, false)]
    [TestCase(MessageOpcode.DisconnectNotice, false)]
    public void IsClientToServer_ForOpcode_FollowsTheDirectionRange(MessageOpcode opcode, bool expected)
    {
        Assert.That(MessageRouting.IsClientToServer(opcode), Is.EqualTo(expected));
    }

    [TestCase(new byte[0])]
    [TestCase(new byte[] { 0x01 })]
    [TestCase(new byte[] { 0x00, 0x00 })]
    [TestCase(new byte[] { 0x40, 0x00 })]
    [TestCase(new byte[] { 0x80, 0x01 })]
    public void TryReadOpcode_ForShortOrUnknownPayload_ReturnsFalse(byte[] payload)
    {
        bool isRead = MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode);

        Assert.That(isRead, Is.False);
        Assert.That(opcode, Is.EqualTo(MessageOpcode.None));
    }

    [Test]
    public void MessageOpcode_Values_KeepTheirStableNumbers()
    {
        string[] expected =
        {
            "None=0x0000", "ClientHello=0x0001", "EnterWorldRequest=0x0002", "MoveInput=0x0003",
            "StopMovement=0x0004", "TargetEntity=0x0005", "AttackEntity=0x0006", "CancelAction=0x0007",
            "UseSkill=0x0008", "PickupItem=0x0009", "Respawn=0x000C", "CreateCharacter=0x000D", "Logout=0x000E",
            "InventoryResyncRequest=0x000F",
            "ServerHello=0x8001", "WorldEntered=0x8003",
            "EntitySpawn=0x8004", "EntityDespawn=0x8005", "EntitySnapshot=0x8006", "TargetChanged=0x8007",
            "AttackStarted=0x8008", "Damage=0x8009", "EntityDied=0x800A", "SkillCastStarted=0x800B",
            "SkillResolved=0x800C", "ItemDropped=0x800D",
            "ItemPickedUp=0x800E", "InventorySnapshot=0x800F",
            "InventoryChanged=0x8010",
            "DisconnectNotice=0x8013",
            "CharacterHealth=0x8014", "EntityRevived=0x8015", "CharacterList=0x8016",
            "CreateCharacterResult=0x8017", "CommandRejected=0x8018",
            "LogoutComplete=0x8019", "CharacterProgress=0x801A", "SkillList=0x801B"
        };

        string[] actual = ((MessageOpcode[])Enum.GetValues(typeof(MessageOpcode)))
            .Select(opcode => $"{opcode}=0x{(ushort)opcode:X4}")
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ProtocolChannel_Values_MatchTheChannelCount()
    {
        var values = (ProtocolChannel[])Enum.GetValues(typeof(ProtocolChannel));
        byte[] channels = values.Select(channel => (byte)channel).ToArray();

        Assert.That(channels, Is.EqualTo(new byte[] { 0, 1, 2 }));
        Assert.That(MessageRouting.ChannelCount, Is.EqualTo(channels.Length));
    }

    [Test]
    public void ProtocolVersion_IsFifteen()
    {
        Assert.That(ProtocolConstants.ProtocolVersion, Is.EqualTo(15));
    }

    [Test]
    public void TryReadOpcode_ForKnownOpcode_ReturnsIt()
    {
        bool isRead = MessageRouting.TryReadOpcode(new byte[] { 0x13, 0x80, 0x01 }, out MessageOpcode opcode);

        Assert.That(isRead, Is.True);
        Assert.That(opcode, Is.EqualTo(MessageOpcode.DisconnectNotice));
    }
}
}
