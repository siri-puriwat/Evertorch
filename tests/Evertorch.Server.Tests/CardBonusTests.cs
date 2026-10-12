using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What worn cards add (Gameplay Systems §2, §11.1): the equipment helper sums the worn rows' cards at the load and
///     at every change of what is worn; their statistics join the items' bonuses and their maxima come after the
///     scaling; their attack, magic attack, and defense join the refine bonuses; the boss modifiers are the last step
///     of a hit on a boss and of a boss's slam; and a card out of place stays, counts, and is logged at the load.
/// </summary>
[TestFixture]
public sealed class CardBonusTests
{
    private const string Sword = "item.weapon.iron_sword";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string Cloth = "item.armor.cloth";
    private const string Keen = "item.card.forest_crawler";
    private const string Hardy = "item.card.training_slime";
    private const string Bane = "item.card.gloom_wisp";
    private const int Spare = 1_000_000;

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");
    private static readonly SkillDefinitionId QuakeSlam = new("skill.quake_slam");

    private static CardBonus Boss(int dealt, int reduction)
    {
        return new CardBonus(new PrimaryStats(0, 0, 0, 0, 0, 0), 0, 0, 0, 0, 0, dealt, reduction);
    }

    // Character 1 enters wearing the given rows, each an item, its slot, and its cards, as earlier sessions left them.
    private static (TestServer Server, ConnectionId Player) EnterWearing(
        params (string Item, string Slot, string[] Cards)[] rows)
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        foreach ((string item, string slot, string[] cards) in rows)
        {
            server.Store.GiveItems(1, item, 1, 1, 0);
            long row = server.Store.Stored(1).Items.Last().Id;
            server.Store.Card(1, row, cards);
            server.Store.Wear(1, row, slot);
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return (server, player);
    }

    private static CharacterSheet LastSheet(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CharacterSheet)
            .Select(message => CharacterSheet.TryRead(message.Payload, out CharacterSheet? sheet) ? sheet! : null!)
            .Last();
    }

    private static MonsterEntity MonarchOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!.Monsters.Single(monster => monster.Definition.Id == Monarch);
    }

    // The first hit of an unarmed player on the Slime Monarch, every draw scripted, with the given cards worn.
    private static int FirstHitOnTheMonarch(CardBonus cards)
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            combatRandom: new SureHitRandom());
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        MonsterEntity monarch = MonarchOf(server);
        server.Place(player, monarch.Position.X - 1.2f, monarch.Position.Z);
        server.PlayerOf(player).CardBonus = cards;
        server.Tick();
        int before = monarch.CurrentHealth;

        server.SendAttack(player, monarch.Id, 1);
        server.TickUntil(() => monarch.CurrentHealth < before);

        return before - monarch.CurrentHealth;
    }

    // Gameplay Systems §11.1: damage to a boss grows by the worn cards' percentage, rounded down.
    [Test]
    public void AHitOnTheMonarch_WithBaneWorn_GrowsByTwentyPercent()
    {
        int plain = FirstHitOnTheMonarch(CardBonus.None);
        int bane = FirstHitOnTheMonarch(Boss(20, 0));

        Assert.That(plain, Is.Positive);
        Assert.That(bane, Is.EqualTo(plain * 120 / 100));
    }

    [Test]
    public void Dress_FromTheBag_TakesTheCardsOfTheWornRowsAlone()
    {
        (TestServer server, ConnectionId player) = EnterWearing((Sword, "Weapon", new[] { Keen }));
        PlayerEntity entity = server.PlayerOf(player);
        var bag = new CharacterInventory(
            1,
            new[]
            {
                new InventoryEntry(1, new ItemDefinitionId(Sword), 1, EquipmentSlot.None, 0,
                    new[] { new ItemDefinitionId(Keen) }),
                new InventoryEntry(2, new ItemDefinitionId(Cloth), 1, EquipmentSlot.Armor, 0,
                    new[] { new ItemDefinitionId(Hardy) })
            });

        WornEquipment.Dress(entity, server.Content, bag);

        Assert.That(
            (entity.CardBonus.Attack, entity.CardBonus.MaxHp, entity.Equipment.FlatAttack),
            Is.EqualTo((0, 20, 0)),
            "the unworn sword's card counts for nothing");
    }

    [Test]
    public void Load_OfACardOutOfPlace_KeepsItCounting_AndLogsIt()
    {
        (TestServer server, ConnectionId player) = EnterWearing(
            (TrainingSword, "Weapon", new[] { Keen, Bane }),
            (Cloth, "Armor", new[] { Keen }));

        PlayerEntity entity = server.PlayerOf(player);
        string[] logged = server.LifetimeLog.Entries
            .Where(entry => entry.EventId.Id == 1040)
            .Select(entry => $"{entry.Fields["Item"]} {entry.Fields["Cards"]} {entry.Fields["Slots"]} "
                + $"{entry.Fields["Misfits"]}")
            .ToArray();

        Assert.That(entity.CardBonus.Attack, Is.EqualTo(16), "both of Keen's attacks count, and Bane");
        Assert.That(entity.CardBonus.BossDamagePercent, Is.EqualTo(20));
        Assert.That(
            logged,
            Is.EqualTo(new[] { $"{TrainingSword} 2 1 none", $"{Cloth} 1 1 {Keen}" }),
            "two cards in one slot, and a weapon's card in armor");
    }

    [Test]
    public void Load_WithCardsWorn_SumsThemIntoTheStatisticsTheCombatAndTheSheet()
    {
        (TestServer bare, ConnectionId bareOne) =
            EnterWearing((Sword, "Weapon", new string[0]), (Cloth, "Armor", new string[0]));
        (TestServer carded, ConnectionId cardedOne) =
            EnterWearing((Sword, "Weapon", new[] { Keen, Bane }), (Cloth, "Armor", new[] { Hardy }));

        PlayerEntity without = bare.PlayerOf(bareOne);
        PlayerEntity with = carded.PlayerOf(cardedOne);
        DerivedStats vitPlusOne = new CharacterStats(new RenewalCharacterRules()).Calculate(
            carded.Content.Jobs[with.Job],
            with.Level,
            new PrimaryStats(with.Primary.Str, with.Primary.Agi, with.Primary.Vit + 1, with.Primary.Int,
                with.Primary.Dex, with.Primary.Luk),
            with.Weapon);
        CharacterSheet sheet = LastSheet(carded, cardedOne);

        Assert.That(
            (with.CardBonus.Attack, with.CardBonus.Stats.Vit, with.CardBonus.MaxHp, with.CardBonus.BossDamagePercent),
            Is.EqualTo((8, 1, 20, 20)));
        Assert.That(
            (with.Equipment.WeaponAttack, with.Equipment.FlatAttack, with.Equipment.HardDefense),
            Is.EqualTo((without.Equipment.WeaponAttack, without.Equipment.FlatAttack + 8,
                without.Equipment.HardDefense)),
            "Keen's attack after the draw; Hardy adds no defense");
        Assert.That(with.MaxHealth, Is.EqualTo(vitPlusOne.MaxHp + 20), "VIT +1 scaled, then max HP +20");
        Assert.That(without.MaxHealth, Is.LessThan(vitPlusOne.MaxHp));
        Assert.That(
            (sheet.EquipmentAttack, sheet.EquipmentVit),
            Is.EqualTo(((ushort)(without.Equipment.WeaponAttack + without.Equipment.FlatAttack + 8), (ushort)1)));
        Assert.That(carded.LifetimeLog.Entries.Where(entry => entry.EventId.Id == 1040), Is.Empty);
    }

    // Gameplay Systems §11.1: a boss's damage shrinks by the worn cards' reduction, at most 90 %, rounded down.
    [Test]
    public void TheMonarchsSlam_OnAPlayerWithRoyalWorn_ShrinksByItsReduction_AtMostNinetyPercent()
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            combatRandom: new SureHitRandom());
        MonsterEntity monarch = MonarchOf(server);
        var players = new List<PlayerEntity>();
        foreach (CardBonus cards in new[] { CardBonus.None, Boss(0, 25), Boss(0, 95) })
        {
            ConnectionId player = server.EnterWorld(players.Count + 1);
            server.CrossIntoTheGrotto(player);
            PlayerEntity entity = server.PlayerOf(player);
            entity.Position = new WorldPosition(monarch.Position.X - 2f, 0f, monarch.Position.Z);
            entity.CurrentHealth = Spare;
            entity.CardBonus = cards;
            players.Add(entity);
        }

        server.Tick();
        server.World.TryGetMap(Grotto, out MapInstance? map);
        server.Combat.BeginMonsterCast(map!, monarch, QuakeSlam, players[0], server.CurrentTick);
        server.Tick(TestServer.TickRate * 3 / 2 + 1);

        int[] lost = players.Select(player => Spare - player.CurrentHealth).ToArray();
        Assert.That(lost[0], Is.Positive);
        Assert.That(lost, Is.EqualTo(new[] { lost[0], lost[0] * 75 / 100, Math.Max(1, lost[0] * 10 / 100) }));
    }
}
}
