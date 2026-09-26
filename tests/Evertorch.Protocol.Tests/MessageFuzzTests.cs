using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Hostile input must never throw, and anything a reader accepts must re-encode to the bytes it came from, so no
///     two different payloads can mean the same message.
/// </summary>
[TestFixture]
public sealed class MessageFuzzTests
{
    private const int Seed = 20260920;
    private const int RandomPayloads = 20000;
    private const int MutationsPerMessage = 5000;

    public delegate byte[]? Reencode(byte[] payload);

    private static IEnumerable<TestCaseData> Messages()
    {
        yield return Case(
            "ClientHello",
            Encode(new ClientHello(1, "0.2.0", 7, "dev:ann")),
            payload => ClientHello.TryRead(payload, out ClientHello? message) ? Encode(message!) : null);
        yield return Case(
            "EnterWorldRequest",
            Encode(new EnterWorldRequest(new CharacterId(42))),
            payload => EnterWorldRequest.TryRead(payload, out EnterWorldRequest message) ? Encode(message) : null);
        yield return Case(
            "TargetEntity",
            Encode(new TargetEntity(new EntityId(42))),
            payload => TargetEntity.TryRead(payload, out TargetEntity message) ? Encode(message) : null);
        yield return Case(
            "ServerHello",
            Encode(new ServerHello(1, "0.2.0", 7, 20, 1234567890123)),
            payload => ServerHello.TryRead(payload, out ServerHello? message) ? Encode(message!) : null);
        yield return Case(
            "WorldEntered",
            Encode(
                new WorldEntered(
                    new MapDefinitionId("map.training_ground"),
                    1,
                    new EntityId(9),
                    new JobDefinitionId("job.adventurer"),
                    100,
                    new WorldPosition(1f, 2f, 3f),
                    new WorldDirection(0f, 1f),
                    5f,
                    60,
                    68,
                    1.5f,
                    41,
                    new CharacterId(7),
                    2,
                    45,
                    50,
                    20,
                    24)),
            payload => WorldEntered.TryRead(payload, out WorldEntered? message) ? Encode(message!) : null);
        yield return Case(
            "EntitySpawn",
            Encode(
                new EntitySpawn(
                    new EntityId(9),
                    EntityKind.Player,
                    "job.adventurer",
                    new WorldPosition(1f, 2f, 3f),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.Moving,
                    0)),
            payload => EntitySpawn.TryRead(payload, out EntitySpawn? message) ? Encode(message!) : null);
        yield return Case(
            "EntitySpawn (monster)",
            Encode(
                new EntitySpawn(
                    new EntityId(10),
                    EntityKind.Monster,
                    "monster.training_slime",
                    new WorldPosition(12f, 0f, 12f),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.Dead,
                    600)),
            payload => EntitySpawn.TryRead(payload, out EntitySpawn? message) ? Encode(message!) : null);
        yield return Case(
            "AttackEntity",
            Encode(new AttackEntity(new EntityId(9), 3)),
            payload => AttackEntity.TryRead(payload, out AttackEntity message) ? Encode(message) : null);
        yield return Case(
            "CancelAction",
            Encode(new CancelAction(4)),
            payload => CancelAction.TryRead(payload, out CancelAction message) ? Encode(message) : null);
        yield return Case(
            "Respawn",
            Encode(new Respawn(5)),
            payload => Respawn.TryRead(payload, out Respawn message) ? Encode(message) : null);
        yield return Case(
            "AttackStarted",
            Encode(
                new AttackStarted(
                    new EntityId(9),
                    new EntityId(10),
                    40,
                    new AttackTiming(
                        TimeSpan.FromMilliseconds(960),
                        TimeSpan.FromMilliseconds(480),
                        TimeSpan.FromMilliseconds(480),
                        TimeSpan.FromMilliseconds(240)))),
            payload => AttackStarted.TryRead(payload, out AttackStarted message) ? Encode(message) : null);
        yield return Case(
            "Damage",
            Encode(new Damage(new EntityId(9), new EntityId(10), CombatResult.Hit, 12, 50, 760)),
            payload => Damage.TryRead(payload, out Damage message) ? Encode(message) : null);
        yield return Case(
            "EntityDied",
            Encode(new EntityDied(new EntityId(10), new EntityId(9), 60)),
            payload => EntityDied.TryRead(payload, out EntityDied message) ? Encode(message) : null);
        yield return Case(
            "CharacterHealth",
            Encode(new CharacterHealth(60, 68, 20, 24)),
            payload => CharacterHealth.TryRead(payload, out CharacterHealth message) ? Encode(message) : null);
        yield return Case(
            "UseSkill",
            Encode(new UseSkill(new SkillDefinitionId("skill.strike"), new EntityId(42), 7)),
            payload => UseSkill.TryRead(payload, out UseSkill? message) ? Encode(message!) : null);
        yield return Case(
            "SkillCastStarted",
            Encode(new SkillCastStarted(new EntityId(9), new SkillDefinitionId("skill.first_aid"), default, 70, 1331)),
            payload => SkillCastStarted.TryRead(payload, out SkillCastStarted? message) ? Encode(message!) : null);
        yield return Case(
            "SkillResolved",
            Encode(
                new SkillResolved(
                    new EntityId(9),
                    new EntityId(42),
                    new SkillDefinitionId("skill.strike"),
                    SkillOutcome.Hit,
                    17,
                    70,
                    660)),
            payload => SkillResolved.TryRead(payload, out SkillResolved? message) ? Encode(message!) : null);
        yield return Case(
            "SkillList",
            Encode(
                new SkillList(
                    new[]
                    {
                        new SkillListEntry(new SkillDefinitionId("skill.strike"), 1.5f, 8, 2000, 500, 1250),
                        new SkillListEntry(new SkillDefinitionId("skill.first_aid"), 0f, 3, 0, 0, 0)
                    })),
            payload => SkillList.TryRead(payload, out SkillList? message) ? Encode(message!) : null);
        yield return Case(
            "StatusEffects",
            Encode(new StatusEffects(new[] { new StatusEffectEntry(new StatusDefinitionId("status.focus"), 42_000) })),
            payload => StatusEffects.TryRead(payload, out StatusEffects? message) ? Encode(message!) : null);
        yield return Case(
            "BuyItem",
            Encode(new BuyItem(new EntityId(13), new ItemDefinitionId("item.weapon.training_sword"), 1, 7)),
            payload => BuyItem.TryRead(payload, out BuyItem? message) ? Encode(message!) : null);
        yield return Case(
            "SellItem",
            Encode(new SellItem(new EntityId(13), 41, 10, 7)),
            payload => SellItem.TryRead(payload, out SellItem message) ? Encode(message) : null);
        yield return Case(
            "NpcServices",
            Encode(
                new NpcServices(
                    new EntityId(13),
                    new[] { new NpcServiceEntry(new ItemDefinitionId("item.consumable.minor_health"), 20, 10) },
                    new[]
                    {
                        new NpcQuestOffer(
                            new QuestDefinitionId("quest.crawler_hunt"),
                            new MonsterDefinitionId("monster.forest_crawler"),
                            5,
                            150,
                            100)
                    })),
            payload => NpcServices.TryRead(payload, out NpcServices? message) ? Encode(message!) : null);
        yield return Case(
            "CharacterProgress",
            Encode(new CharacterProgress(2, 45, 50)),
            payload => CharacterProgress.TryRead(payload, out CharacterProgress message) ? Encode(message) : null);
        yield return Case(
            "EntityRevived",
            Encode(new EntityRevived(new EntityId(9), new WorldPosition(1f, 2f, 3f), new WorldDirection(0f, 1f), 70)),
            payload => EntityRevived.TryRead(payload, out EntityRevived message) ? Encode(message) : null);
        yield return Case(
            "PickupItem",
            Encode(new PickupItem(new EntityId(7), 9)),
            payload => PickupItem.TryRead(payload, out PickupItem message) ? Encode(message) : null);
        yield return Case(
            "EquipItem",
            Encode(new EquipItem(41, 9)),
            payload => EquipItem.TryRead(payload, out EquipItem message) ? Encode(message) : null);
        yield return Case(
            "UnequipItem",
            Encode(new UnequipItem(EquipmentSlot.Weapon, 9)),
            payload => UnequipItem.TryRead(payload, out UnequipItem message) ? Encode(message) : null);
        yield return Case(
            "UseItem",
            Encode(new UseItem(41, 9)),
            payload => UseItem.TryRead(payload, out UseItem message) ? Encode(message) : null);
        yield return Case(
            "ItemPickedUp",
            Encode(new ItemPickedUp(new EntityId(7), new EntityId(3), new ItemDefinitionId("item.material.slime_gel"),
                2)),
            payload => ItemPickedUp.TryRead(payload, out ItemPickedUp? message) ? Encode(message!) : null);
        yield return Case(
            "InventoryResyncRequest",
            Encode(new InventoryResyncRequest()),
            payload => InventoryResyncRequest.TryRead(payload, out InventoryResyncRequest message)
                ? Encode(message)
                : null);
        yield return Case(
            "InventorySnapshot",
            Encode(
                new InventorySnapshot(
                    7,
                    250,
                    1,
                    3,
                    new[]
                    {
                        new InventoryEntry(11, new ItemDefinitionId("item.material.slime_gel"), 999),
                        new InventoryEntry(12, new ItemDefinitionId("item.a"), 1)
                    })),
            payload => InventorySnapshot.TryRead(payload, out InventorySnapshot? message) ? Encode(message!) : null);
        yield return Case(
            "InventoryChanged",
            Encode(
                new InventoryChanged(
                    7,
                    8,
                    250,
                    new[]
                    {
                        new InventoryEntry(11, new ItemDefinitionId("item.material.slime_gel"), 0),
                        new InventoryEntry(12, new ItemDefinitionId("item.a"), 2)
                    })),
            payload => InventoryChanged.TryRead(payload, out InventoryChanged? message) ? Encode(message!) : null);
        yield return Case(
            "CommandRejected",
            Encode(new CommandRejected(0x01020304, CommandRejectionReason.Busy)),
            payload => CommandRejected.TryRead(payload, out CommandRejected message) ? Encode(message) : null);
        yield return Case(
            "Logout",
            Encode(new Logout(0x01020304)),
            payload => Logout.TryRead(payload, out Logout message) ? Encode(message) : null);
        yield return Case(
            "LogoutComplete",
            Encode(new LogoutComplete()),
            payload => LogoutComplete.TryRead(payload, out LogoutComplete message) ? Encode(message) : null);
        yield return Case(
            "CreateCharacter",
            Encode(new CreateCharacter("Ann0")),
            payload => CreateCharacter.TryRead(payload, out CreateCharacter? message) ? Encode(message!) : null);
        yield return Case(
            "CreateCharacterResult",
            Encode(new CreateCharacterResult(CreateCharacterOutcome.Created, new CharacterId(9))),
            payload => CreateCharacterResult.TryRead(payload, out CreateCharacterResult message)
                ? Encode(message)
                : null);
        yield return Case(
            "CharacterList",
            Encode(
                new CharacterList(
                    new[]
                    {
                        new CharacterListEntry(new CharacterId(7), "Ann0", new JobDefinitionId("job.adventurer"), 1),
                        new CharacterListEntry(new CharacterId(8), "Bob12", new JobDefinitionId("job.adventurer"), 12)
                    })),
            payload => CharacterList.TryRead(payload, out CharacterList? message) ? Encode(message!) : null);
        yield return Case(
            "ItemDropped",
            Encode(new ItemDropped(new EntityId(11), "item.material.slime_gel", 2, new WorldPosition(1f, 2f, 3f))),
            payload => ItemDropped.TryRead(payload, out ItemDropped? message) ? Encode(message!) : null);
        yield return Case(
            "TargetChanged",
            Encode(new TargetChanged(new EntityId(9), new EntityId(10))),
            payload => TargetChanged.TryRead(payload, out TargetChanged message) ? Encode(message) : null);
        yield return Case(
            "EntityDespawn",
            Encode(new EntityDespawn(new EntityId(9), DespawnReason.OutOfRange)),
            payload => EntityDespawn.TryRead(payload, out EntityDespawn message) ? Encode(message) : null);
        yield return Case(
            "MoveInput",
            Encode(new MoveInput(new MoveIntent(3, 4, 0.6f, -0.8f))),
            payload => MoveInput.TryRead(payload, out MoveInput message) ? Encode(message) : null);
        yield return Case(
            "StopMovement",
            Encode(new StopMovement(3, 4)),
            payload => StopMovement.TryRead(payload, out StopMovement message) ? Encode(message) : null);
        yield return Case(
            "EntitySnapshot",
            Encode(
                new EntitySnapshot(
                    100,
                    3,
                    new[]
                    {
                        new EntityState(
                            new EntityId(9),
                            new WorldPosition(1f, 2f, 3f),
                            new WorldDirection(0f, 1f),
                            5f,
                            0f,
                            0f,
                            EntityStateFlags.Moving),
                        new EntityState(new EntityId(10), default, new WorldDirection(1f, 0f), 0f, 0f, 0f, 0)
                    })),
            payload => EntitySnapshot.TryRead(payload, out EntitySnapshot? message) ? Encode(message!) : null);
        yield return Case(
            "DisconnectNotice",
            Encode(new DisconnectNotice(DisconnectReason.Kicked, "Bye")),
            payload => DisconnectNotice.TryRead(payload, out DisconnectNotice? message) ? Encode(message!) : null);
    }

    [TestCaseSource(nameof(Messages))]
    public void TryRead_ForRandomBytes_NeverThrowsAndAcceptsOnlyCanonicalPayloads(byte[] valid, Reencode reencode)
    {
        var random = new Random(Seed);
        byte[] opcode = { valid[0], valid[1] };

        for (int iteration = 0; iteration < RandomPayloads; iteration++)
        {
            byte[] payload = new byte[random.Next(0, 128)];
            random.NextBytes(payload);

            // Half the payloads carry the right opcode, or nearly all of them would fail on the first two bytes.
            if (payload.Length >= 2 && (iteration & 1) == 0)
            {
                payload[0] = opcode[0];
                payload[1] = opcode[1];
            }

            AssertCanonicalOrRejected(payload, reencode);
        }
    }

    [TestCaseSource(nameof(Messages))]
    public void TryRead_ForMutatedValidPayload_NeverThrowsAndAcceptsOnlyCanonicalPayloads(
        byte[] valid,
        Reencode reencode)
    {
        var random = new Random(Seed);
        Assert.That(reencode(valid), Is.EqualTo(valid), "the unmutated payload must be accepted");

        for (int iteration = 0; iteration < MutationsPerMessage; iteration++)
        {
            byte[] payload = (byte[])valid.Clone();
            // A message that is only its opcode has no body to mutate; resizing still exercises it.
            int changes = payload.Length > 2 ? random.Next(1, 4) : 0;
            for (int change = 0; change < changes; change++)
            {
                payload[random.Next(2, payload.Length)] = (byte)random.Next(256);
            }

            if (random.Next(4) == 0)
            {
                Array.Resize(ref payload, random.Next(0, payload.Length + 4));
            }

            AssertCanonicalOrRejected(payload, reencode);
        }
    }

    private static void AssertCanonicalOrRejected(byte[] payload, Reencode reencode)
    {
        byte[]? reencoded = null;
        Action read = () => reencoded = reencode(payload);

        Assert.That(read, Throws.Nothing, BitConverter.ToString(payload));
        if (reencoded != null)
        {
            Assert.That(reencoded, Is.EqualTo(payload), "accepted a payload that is not its own encoding");
        }
    }

    private static TestCaseData Case(string name, byte[] valid, Reencode reencode)
    {
        return new TestCaseData(valid, reencode).SetArgDisplayNames(name);
    }

    private static byte[] Encode(ClientHello message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(ServerHello message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(WorldEntered message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntitySpawn message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(PickupItem message)
    {
        byte[] buffer = new byte[PickupItem.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EquipItem message)
    {
        byte[] buffer = new byte[EquipItem.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(UnequipItem message)
    {
        byte[] buffer = new byte[UnequipItem.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(UseItem message)
    {
        byte[] buffer = new byte[UseItem.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(ItemPickedUp message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(InventoryResyncRequest message)
    {
        byte[] buffer = new byte[InventoryResyncRequest.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(InventorySnapshot message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(InventoryChanged message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(CommandRejected message)
    {
        byte[] buffer = new byte[CommandRejected.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(Logout message)
    {
        byte[] buffer = new byte[Logout.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(LogoutComplete message)
    {
        byte[] buffer = new byte[LogoutComplete.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(CreateCharacter message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(CreateCharacterResult message)
    {
        byte[] buffer = new byte[CreateCharacterResult.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(CharacterList message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(ItemDropped message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(DisconnectNotice message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntitySnapshot message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(MoveInput message)
    {
        byte[] buffer = new byte[MoveInput.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(StopMovement message)
    {
        byte[] buffer = new byte[StopMovement.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EnterWorldRequest message)
    {
        byte[] buffer = new byte[EnterWorldRequest.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(AttackStarted message)
    {
        byte[] buffer = new byte[AttackStarted.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(Damage message)
    {
        byte[] buffer = new byte[Damage.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntityDied message)
    {
        byte[] buffer = new byte[EntityDied.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(CharacterHealth message)
    {
        byte[] buffer = new byte[CharacterHealth.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(UseSkill message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(SkillCastStarted message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(SkillResolved message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(BuyItem message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(SellItem message)
    {
        byte[] buffer = new byte[SellItem.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(NpcServices message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(StatusEffects message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(SkillList message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(CharacterProgress message)
    {
        byte[] buffer = new byte[CharacterProgress.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntityRevived message)
    {
        byte[] buffer = new byte[EntityRevived.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(AttackEntity message)
    {
        byte[] buffer = new byte[AttackEntity.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(CancelAction message)
    {
        byte[] buffer = new byte[CancelAction.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(Respawn message)
    {
        byte[] buffer = new byte[Respawn.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(TargetChanged message)
    {
        byte[] buffer = new byte[TargetChanged.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntityDespawn message)
    {
        byte[] buffer = new byte[EntityDespawn.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(TargetEntity message)
    {
        byte[] buffer = new byte[TargetEntity.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    [Test]
    public void Messages_AcrossTheCases_NameEveryOpcode()
    {
        var fuzzed = new HashSet<MessageOpcode>(
            Messages()
                .Select(data => (byte[])data.Arguments[0]!)
                .Select(valid => (MessageOpcode)BinaryPrimitives.ReadUInt16LittleEndian(valid)));
        MessageOpcode[] opcodes = Enum.GetValues<MessageOpcode>()
            .Where(opcode => opcode != MessageOpcode.None)
            .ToArray();

        Assert.That(opcodes.Except(fuzzed), Is.Empty, "opcodes without a fuzz case");
        Assert.That(fuzzed.Except(opcodes), Is.Empty, "fuzz cases of no opcode");
    }
}
}
