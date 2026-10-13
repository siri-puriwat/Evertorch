using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Combat builds each hit's modifiers from the fighters (Gameplay Systems §3, §9, §11.1, §11.7): a player's sword
///     meets a monster's size, the player's attack element and a skill's own meet the monster's element, and the worn
///     cards' groups match the monster's element, race, and size; a monster's skill meets the player's armor element and
///     the cards' reduction of its element; and a hit that deals nothing for its element is immune, told to everyone who
///     sees it, and changes nothing.
/// </summary>
[TestFixture]
public sealed class ElementCombatTests
{
    private const string ArcaneBolt = "skill.arcane_bolt";
    private const string Strike = "skill.strike";
    private const int Spare = 1_000_000;

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MapDefinitionId TrainingGround = new("map.training_ground");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");
    private static readonly SkillDefinitionId QuakeSlam = new("skill.quake_slam");
    private static readonly StatusDefinitionId Converted = new("status.focus");

    private static CardBonus Cards(
        IReadOnlyDictionary<Race, int>? races = null,
        IReadOnlyDictionary<Element, int>? elements = null,
        IReadOnlyDictionary<BodySize, int>? sizes = null,
        IReadOnlyDictionary<Element, int>? reductions = null,
        Element armor = Element.Neutral)
    {
        return new CardBonus(
            new PrimaryStats(0, 0, 0, 0, 0, 0),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            races,
            elements,
            sizes,
            reductions,
            armor);
    }

    private static MonsterEntity MonarchOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!.Monsters.Single(monster => monster.Definition.Id == Monarch);
    }

    // The first swing, or the first Strike, of a player beside the Slime Monarch, every draw scripted: a weapon attack
    // of 38 drawn at its least, 37, beside a status attack of 7, against the Monarch's defense of 40.
    private static int FirstHitOnTheMonarch(
        string? weapon = null,
        Element? attackElement = null,
        CardBonus? cards = null,
        bool isStrike = false,
        IRandomSource? random = null)
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            combatRandom: random ?? new SureHitRandom());
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        MonsterEntity monarch = MonarchOf(server);
        server.Place(player, monarch.Position.X - 1.2f, monarch.Position.Z);
        PlayerEntity entity = server.PlayerOf(player);
        entity.Weapon = weapon == null ? null : server.Content.Items[new ItemDefinitionId(weapon)].Equipment;
        entity.Equipment = new WornEquipment(38, 0, 0, 0);
        entity.CardBonus = cards ?? CardBonus.None;
        if (attackElement.HasValue)
        {
            entity.StartStatusEffect(new ActiveStatusEffect(Converted, default, long.MaxValue, attackElement.Value));
        }

        if (isStrike)
        {
            entity.SetSkillLevel(new SkillDefinitionId(Strike), 1);
            entity.CurrentSpirit = entity.MaxSpirit;
        }

        server.Tick();
        int before = monarch.CurrentHealth;

        if (isStrike)
        {
            server.SendUseSkill(player, Strike, monarch.Id, 1);
        }
        else
        {
            server.SendAttack(player, monarch.Id, 1);
        }

        server.TickUntil(() => monarch.CurrentHealth < before);
        return before - monarch.CurrentHealth;
    }

    private static SkillResolved[] ResolvedTo(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.SkillResolved)
            .Select(message => SkillResolved.TryRead(message.Payload, out SkillResolved? resolved) ? resolved! : null!)
            .ToArray();
    }

    // No new draw (Gameplay Systems §3, §11.7): a converted swing and Strike with every kind of card group draw the very
    // numbers a plain one draws, so a seed replays whatever the elements and the cards.
    [TestCase(false)]
    [TestCase(true)]
    public void AConvertedCardedHit_DrawsTheNumbersAPlainOneDraws(bool isStrike)
    {
        var plain = new ScriptedRandom(0);
        var dressed = new ScriptedRandom(0);
        CardBonus cards = Cards(
            new Dictionary<Race, int> { [Race.Plant] = 10 },
            new Dictionary<Element, int> { [Element.Water] = 10 },
            new Dictionary<BodySize, int> { [BodySize.Large] = 10 },
            new Dictionary<Element, int> { [Element.Earth] = 10 },
            Element.Earth);

        FirstHitOnTheMonarch("item.weapon.iron_sword", isStrike: isStrike, random: plain);
        FirstHitOnTheMonarch("item.weapon.iron_sword", Element.Wind, cards, isStrike, dressed);

        Assert.That(dressed.Bounds, Is.EqualTo(plain.Bounds).And.Not.Empty);
    }

    // Gameplay Systems §3: a Holy bolt on a Holy slime deals nothing and is immune; everyone who sees the slime is told,
    // and the slime keeps its health, turns on no one, and logs no damage.
    [Test]
    public void AHolyBolt_OnAHolySlime_IsImmune_ToEveryoneWhoSeesIt_AndChangesNothing()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, "monsters.json", "monster.training_slime", "element", "\"holy\"");
        PackageFixture.SetValue(files, "skills.json", ArcaneBolt, "element", "\"holy\"");
        var server = new TestServer(
            withMonsters: true,
            withMonsterAi: false,
            content: ServerContentLoader.Load(files));
        ConnectionId caster = server.EnterWorld(1);
        ConnectionId observer = server.EnterWorld(2);
        server.World.TryGetMap(TrainingGround, out MapInstance? map);
        MonsterEntity slime = map!.Monsters.OrderBy(monster => monster.Id.Value).First();
        server.Place(caster, slime.Position.X - 3f, slime.Position.Z);
        server.Place(observer, slime.Position.X + 3f, slime.Position.Z);
        PlayerEntity arcanist = server.PlayerOf(caster);
        arcanist.ChangeJob(new JobDefinitionId("job.arcanist"));
        arcanist.SetSkillLevel(new SkillDefinitionId(ArcaneBolt), 1);
        arcanist.CurrentSpirit = arcanist.MaxSpirit;
        server.Tick();
        int health = slime.CurrentHealth;

        server.SendUseSkill(caster, ArcaneBolt, slime.Id, 1);
        server.Tick(TestServer.TickRate * 2);

        Assert.That(
            ResolvedTo(server, caster).Concat(ResolvedTo(server, observer))
                .Select(resolved => (resolved.Target, resolved.Outcome, resolved.Amount)),
            Is.EqualTo(new[] { (slime.Id, SkillOutcome.Immune, 0u), (slime.Id, SkillOutcome.Immune, 0u) }));
        Assert.That(
            (slime.CurrentHealth, slime.Brain.LastAttacker, slime.DamageLog.Count),
            Is.EqualTo((health, default(EntityId), 0)));
    }

    // Gameplay Systems §11.1: a sword deals three quarters of its draw to a Small or a Large monster; a staff all of it.
    [Test]
    public void ASwordSwing_OnTheLargeMonarch_SizesItsDrawToThreeQuarters()
    {
        int staff = FirstHitOnTheMonarch("item.weapon.training_staff");
        int sword = FirstHitOnTheMonarch("item.weapon.training_sword");
        int bare = FirstHitOnTheMonarch();

        Assert.That(
            (staff, sword, bare),
            Is.EqualTo((46, 37, 46)),
            "14 + 37 = 51, and 14 + ⌊37 × 75 %⌋ = 41, less the defense of 40");
    }

    // Gameplay Systems §3, §9.1: the player's attack element meets the Water Monarch's on the weapon's part alone, and a
    // Strike, a skill of the weapon's element, takes it too.
    [Test]
    public void AnAttackElement_OnTheWaterMonarch_MeetsItsElement_ASwingAndAStrikeAlike()
    {
        int wind = FirstHitOnTheMonarch(attackElement: Element.Wind);
        int water = FirstHitOnTheMonarch(attackElement: Element.Water);
        int strike = FirstHitOnTheMonarch(isStrike: true);
        int windStrike = FirstHitOnTheMonarch(attackElement: Element.Wind, isStrike: true);

        Assert.That((wind, water), Is.EqualTo((63, 21)), "14 + ⌊37 × 150 %⌋ = 69, and 14 + ⌊37 × 25 %⌋ = 23");
        Assert.That((strike, windStrike), Is.EqualTo((60, 81)), "51 and 69 at 130 %");
    }

    // Gameplay Systems §11.7: the worn cards' groups grow the weapon's part by the Monarch's element, race, and size,
    // each rounded down in turn; a group naming another race adds nothing.
    [Test]
    public void TheCardsGroups_OnTheMonarch_MatchItsElementRaceAndSize()
    {
        int matched = FirstHitOnTheMonarch(
            cards: Cards(
                new Dictionary<Race, int> { [Race.Plant] = 10 },
                new Dictionary<Element, int> { [Element.Water] = 10 },
                new Dictionary<BodySize, int> { [BodySize.Large] = 10 }));
        int otherRace = FirstHitOnTheMonarch(cards: Cards(new Dictionary<Race, int> { [Race.Insect] = 10 }));

        Assert.That((matched, otherRace), Is.EqualTo((56, 46)), "37 grows to 40, 44, and 48: 14 + 48 = 62");
    }

    // Gameplay Systems §3, §9: the Monarch's Earth slam meets each player's armor element after the defense, then the
    // cards' reduction of Earth.
    [Test]
    public void TheMonarchsSlam_MeetsEachPlayersArmorElement_ThenTheirReductionOfEarth()
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            combatRandom: new SureHitRandom());
        MonsterEntity monarch = MonarchOf(server);
        var players = new List<PlayerEntity>();
        CardBonus[] worn =
        {
            CardBonus.None, Cards(armor: Element.Earth), Cards(armor: Element.Wind),
            Cards(reductions: new Dictionary<Element, int> { [Element.Earth] = 30, [Element.Fire] = 50 })
        };
        foreach (CardBonus cards in worn)
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
        Assert.That(
            lost,
            Is.EqualTo(new[] { lost[0], lost[0] * 25 / 100, lost[0] * 150 / 100, lost[0] * 70 / 100 }),
            "Earth on Earth a quarter, on Wind half again; a reduction of Earth alone counts");
    }
}
}
