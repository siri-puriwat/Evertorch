using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class EntitySpawnTests
{
    private const int KindOffset = 10;
    private const int DefinitionTextOffset = 13;
    private const int PositionXOffset = 18;
    private const int FlagsOffset = 38;

    private static readonly byte[] NotANumber = { 0x00, 0x00, 0xC0, 0x7F };

    private static readonly byte[] GoldenBytes =
    {
        0x04, 0x80,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x01,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x61,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x3F, 0x00, 0x00, 0x00, 0xC0,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x01, 0x00,
        0x00, 0x00,
        0x00, 0x00,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61
    };

    private static readonly byte[] MonsterBytes =
    {
        0x04, 0x80,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02,
        0x09, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x61,
        0x00, 0x00, 0x40, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x41,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00,
        0xE8, 0x03,
        0x00, 0x00,
        0x00, 0x00
    };

    private static EntitySpawn Monster => new(
        new EntityId(42),
        EntityKind.Monster,
        "monster.a",
        new WorldPosition(12f, 0f, 12f),
        new WorldDirection(0f, 1f),
        EntityStateFlags.None,
        1000);

    private static EntitySpawn Golden => new(
        new EntityId(0x0123456789ABCDEF),
        EntityKind.Player,
        "job.a",
        new WorldPosition(1f, 0.5f, -2f),
        new WorldDirection(0f, 1f),
        EntityStateFlags.Moving,
        0,
        "",
        "Anna");

    private static EntitySpawn PlayerNamed(string name)
    {
        return new EntitySpawn(
            new EntityId(7),
            EntityKind.Player,
            "job.adventurer",
            new WorldPosition(1f, 0f, 1f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0,
            "",
            name);
    }

    private static byte[] Encode(EntitySpawn message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        message.Write(bytes);
        return bytes;
    }

    [TestCase(0)]
    [TestCase(5)]
    [TestCase(255)]
    public void TryRead_WhenKindIsUnknown_ReturnsFalse(byte kind)
    {
        Assert.That(EntitySpawn.TryRead(WireMatrix.With(GoldenBytes, KindOffset, kind), out _), Is.False);
    }

    [TestCase(0x04, 0x00)]
    [TestCase(0x01, 0x80)]
    [TestCase(0xFF, 0xFF)]
    public void TryRead_WhenFlagsContainUnknownBits_ReturnsFalse(byte low, byte high)
    {
        Assert.That(EntitySpawn.TryRead(WireMatrix.With(GoldenBytes, FlagsOffset, low, high), out _), Is.False);
    }

    [Test]
    public void ItemDrop_WithAnItemId_RoundTripsAndRefusesOtherIdsAndHealth()
    {
        var drop = new EntitySpawn(
            new EntityId(11),
            EntityKind.ItemDrop,
            "item.material.slime_gel",
            new WorldPosition(12.5f, 0f, 11.5f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0);
        byte[] bytes = new byte[drop.GetEncodedLength()];
        drop.Write(bytes);
        byte[] withHealth = WireMatrix.With(bytes, bytes.Length - 6, 0x01, 0x00);
        byte[] monsterAsDrop = WireMatrix.With(WireMatrix.With(MonsterBytes, KindOffset, 0x03), 44, 0x00, 0x00);

        Assert.That(EntitySpawn.TryRead(bytes, out EntitySpawn? read), Is.True);
        Assert.That(read!.Kind, Is.EqualTo(EntityKind.ItemDrop));
        Assert.That(read.DefinitionId, Is.EqualTo("item.material.slime_gel"));
        Assert.That(EntitySpawn.TryRead(withHealth, out _), Is.False);
        Assert.That(EntitySpawn.TryRead(monsterAsDrop, out _), Is.False);
    }

    [Test]
    public void Largest_IsOneHundredNinetyTwoBytes()
    {
        var largest = new EntitySpawn(
            new EntityId(long.MaxValue),
            EntityKind.Player,
            $"job.{new string('a', DefinitionIdLimits.MaxLength - 4)}",
            new WorldPosition(1f, 0f, 1f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0,
            $"item.{new string('a', DefinitionIdLimits.MaxLength - 5)}",
            new string('A', CharacterNames.MaxLength));

        byte[] bytes = Encode(largest);

        Assert.That(bytes, Has.Length.EqualTo(192));
        Assert.That(EntitySpawn.TryRead(bytes, out _), Is.True);
    }

    [Test]
    public void Npc_WithAnNpcId_RoundTripsAndRefusesOtherIdsAndHealth()
    {
        var npc = new EntitySpawn(
            new EntityId(13),
            EntityKind.Npc,
            "npc.quartermaster",
            new WorldPosition(-3.5f, 0f, 4.5f),
            new WorldDirection(0.6f, -0.8f),
            EntityStateFlags.None,
            0);
        byte[] bytes = new byte[npc.GetEncodedLength()];
        npc.Write(bytes);
        byte[] withHealth = WireMatrix.With(bytes, bytes.Length - 6, 0x01, 0x00);
        byte[] monsterAsNpc = WireMatrix.With(WireMatrix.With(MonsterBytes, KindOffset, 0x04), 44, 0x00, 0x00);

        Assert.That(EntitySpawn.TryRead(bytes, out EntitySpawn? read), Is.True);
        Assert.That(read!.Kind, Is.EqualTo(EntityKind.Npc));
        Assert.That(read.DefinitionId, Is.EqualTo("npc.quartermaster"));
        Assert.That(EntitySpawn.TryRead(withHealth, out _), Is.False, "an NPC shares no HP");
        Assert.That(EntitySpawn.TryRead(monsterAsNpc, out _), Is.False, "a monster ID is not an NPC's");
    }

    [Test]
    public void Player_WithAValidName_RoundTripsIt_AndNoOtherKindCarriesOne()
    {
        Assert.That(EntitySpawn.TryRead(Encode(PlayerNamed("Abcdefghijklmnopqrstuvw")), out EntitySpawn? longest),
            Is.True);
        Assert.That(longest!.Name, Is.EqualTo("Abcdefghijklmnopqrstuvw"));
        Assert.That(EntitySpawn.TryRead(Encode(PlayerNamed(string.Empty)), out _), Is.False, "a player has a name");
        Assert.That(EntitySpawn.TryRead(Encode(PlayerNamed("Abc")), out _), Is.False, "too short");
        Assert.That(EntitySpawn.TryRead(Encode(PlayerNamed("Ab cd")), out _), Is.False, "not a letter or digit");
        Assert.That(EntitySpawn.TryRead(Encode(PlayerNamed("Ab<b>")), out _), Is.False, "no markup");
        var namedMonster = new EntitySpawn(
            new EntityId(8),
            EntityKind.Monster,
            "monster.a",
            new WorldPosition(1f, 0f, 1f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            1000,
            "",
            "Anna");
        Assert.That(EntitySpawn.TryRead(Encode(namedMonster), out _), Is.False, "only a player is named");
    }

    [Test]
    public void Player_WithAWornWeapon_RoundTripsIt_AndNoOtherKindCarriesOne()
    {
        var armed = new EntitySpawn(
            new EntityId(7),
            EntityKind.Player,
            "job.adventurer",
            new WorldPosition(1f, 0f, 1f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0,
            "item.weapon.training_sword",
            "Wearer1");
        var armedMonster = new EntitySpawn(
            new EntityId(8),
            EntityKind.Monster,
            "monster.a",
            new WorldPosition(1f, 0f, 1f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            1000,
            "item.weapon.training_sword");
        var badWeapon = new EntitySpawn(
            new EntityId(9),
            EntityKind.Player,
            "job.adventurer",
            new WorldPosition(1f, 0f, 1f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0,
            "Training Sword",
            "Wearer2");
        byte[] bytes = new byte[armed.GetEncodedLength()];
        armed.Write(bytes);
        byte[] monster = new byte[armedMonster.GetEncodedLength()];
        armedMonster.Write(monster);
        byte[] bad = new byte[badWeapon.GetEncodedLength()];
        badWeapon.Write(bad);

        Assert.That(EntitySpawn.TryRead(bytes, out EntitySpawn? read), Is.True);
        Assert.That(read!.WornWeapon, Is.EqualTo("item.weapon.training_sword"));
        Assert.That(EntitySpawn.TryRead(monster, out _), Is.False, "only a player wears a weapon");
        Assert.That(EntitySpawn.TryRead(bad, out _), Is.False, "a worn weapon is an item ID");
        Assert.That(EntitySpawn.TryRead(GoldenBytes, out EntitySpawn? unarmed), Is.True);
        Assert.That(unarmed!.WornWeapon, Is.Empty, "unarmed");
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = EntitySpawn.TryRead(GoldenBytes, out EntitySpawn? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.Entity, Is.EqualTo(new EntityId(0x0123456789ABCDEF)));
        Assert.That(message.Kind, Is.EqualTo(EntityKind.Player));
        Assert.That(message.DefinitionId, Is.EqualTo("job.a"));
        Assert.That(message.Position, Is.EqualTo(new WorldPosition(1f, 0.5f, -2f)));
        Assert.That(message.Facing, Is.EqualTo(new WorldDirection(0f, 1f)));
        Assert.That(message.StateFlags, Is.EqualTo(EntityStateFlags.Moving));
        Assert.That(message.Name, Is.EqualTo("Anna"));
    }

    [Test]
    public void TryRead_ForMonsterBytes_ReturnsKnownMonster()
    {
        bool isRead = EntitySpawn.TryRead(MonsterBytes, out EntitySpawn? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.Kind, Is.EqualTo(EntityKind.Monster));
        Assert.That(message.DefinitionId, Is.EqualTo("monster.a"));
        Assert.That(message.Position, Is.EqualTo(new WorldPosition(12f, 0f, 12f)));
        Assert.That(message.HealthPermille, Is.EqualTo(1000));
    }

    [Test]
    public void TryRead_WhenAnyFloatIsNotFinite_ReturnsFalse()
    {
        for (int offset = PositionXOffset; offset < FlagsOffset; offset += sizeof(float))
        {
            Assert.That(EntitySpawn.TryRead(WireMatrix.With(GoldenBytes, offset, NotANumber), out _), Is.False);
        }
    }

    [Test]
    public void TryRead_WhenDefinitionIsNotAJobForAPlayer_ReturnsFalse()
    {
        byte[] mapId = WireMatrix.With(GoldenBytes, DefinitionTextOffset, 0x6D, 0x61, 0x70);

        Assert.That(EntitySpawn.TryRead(mapId, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenDefinitionIsNotAMonsterForAMonster_ReturnsFalse()
    {
        byte[] playerKind = WireMatrix.With(MonsterBytes, KindOffset, 0x01);
        byte[] jobForMonster = WireMatrix.With(GoldenBytes, KindOffset, 0x02);

        Assert.That(EntitySpawn.TryRead(playerKind, out _), Is.False);
        Assert.That(EntitySpawn.TryRead(jobForMonster, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenHealthDoesNotFitTheKind_ReturnsFalse()
    {
        const int healthOffset = 44;
        byte[] playerWithHealth = WireMatrix.With(GoldenBytes, 40, 0x01, 0x00);
        byte[] monsterAboveFull = WireMatrix.With(MonsterBytes, healthOffset, 0xE9, 0x03);

        Assert.That(EntitySpawn.TryRead(playerWithHealth, out _), Is.False);
        Assert.That(EntitySpawn.TryRead(monsterAboveFull, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => EntitySpawn.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => EntitySpawn.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => EntitySpawn.TryRead(bytes, out _));
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[Golden.GetEncodedLength()];

        int written = Golden.Write(buffer);

        Assert.That(written, Is.EqualTo(GoldenBytes.Length));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void Write_ForKnownMonster_ProducesMonsterBytes()
    {
        byte[] buffer = new byte[Monster.GetEncodedLength()];

        int written = Monster.Write(buffer);

        Assert.That(written, Is.EqualTo(MonsterBytes.Length));
        Assert.That(buffer, Is.EqualTo(MonsterBytes));
    }

    [Test]
    public void Write_WhenDefinitionIdExceedsLimit_Throws()
    {
        var message =
            new EntitySpawn(default, EntityKind.Player, $"job.{new string('a', 61)}", default, default, 0, 0);
        Action write = () => message.Write(new byte[512]);

        Assert.That(write, Throws.ArgumentException);
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[GoldenBytes.Length - 1]);

        Assert.That(write, Throws.ArgumentException);
    }

    [Test]
    public void Write_WhenNameExceedsLimit_Throws()
    {
        Action write = () => PlayerNamed(new string('A', CharacterNames.MaxLength + 1)).Write(new byte[512]);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
