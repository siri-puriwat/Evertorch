using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Ashen Quarry's monsters on the server (Gameplay Systems §3, §9, §9.1, §10; Milestone 21 owner decisions 19 to
///     23): the Cinder Crawler's Fire bite against the armor's element and the Fireward card; a swing on a Fire body
///     with the Dousing card or a Water weapon; the Basalt Crawler's stun and its partner's answer; the Ash Wisp's
///     distance and silence; and the Furnace Heart's two telegraphed casts, the delay after them, its immunity to every
///     ailment, and a leash that no fight inside its arena reaches.
/// </summary>
[TestFixture]
public sealed class QuarryCombatTests
{
    private const int Spare = 1_000_000;
    private const string CinderCrawler = "monster.cinder_crawler";
    private const string BasaltCrawler = "monster.basalt_crawler";
    private const string AshWisp = "monster.ash_wisp";
    private const string FurnaceHeart = "monster.furnace_heart";

    private static readonly MapDefinitionId Quarry = new("map.ashen_quarry");
    private static readonly SkillDefinitionId CinderBite = new("skill.cinder_bite");
    private static readonly SkillDefinitionId BasaltBash = new("skill.basalt_bash");
    private static readonly SkillDefinitionId Hush = new("skill.hush");
    private static readonly SkillDefinitionId FurnaceBurst = new("skill.furnace_burst");
    private static readonly SkillDefinitionId AshCloud = new("skill.ash_cloud");

    private static MapInstance QuarryOf(TestServer server)
    {
        server.World.TryGetMap(Quarry, out MapInstance? map);
        return map!;
    }

    private static MonsterEntity[] AllOf(TestServer server, string monster)
    {
        return QuarryOf(server).Monsters.Where(candidate => candidate.Definition.Id.Value == monster).ToArray();
    }

    private static CardBonus CardOf(TestServer server, string card)
    {
        return server.Content.Items[new ItemDefinitionId(card)].Card!.Bonus;
    }

    private static float Distance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // A player for each distance, west of the first monster of a kind, in the quarry, each with HP to spare.
    private static (TestServer Server, ConnectionId[] Players, MonsterEntity Monster) Beside(
        string monster,
        IRandomSource random,
        bool withMonsterAi,
        params float[] distances)
    {
        var server = new TestServer(
            withEveryMap: true,
            withQuarry: true,
            withMonsters: true,
            withMonsterAi: withMonsterAi,
            combatRandom: random);
        MonsterEntity found = AllOf(server, monster)[0];
        var players = new ConnectionId[distances.Length];
        for (int index = 0; index < distances.Length; index++)
        {
            players[index] = server.EnterWorld(index + 1);
            server.CrossIntoTheQuarry(players[index]);
            PlayerEntity player = server.PlayerOf(players[index]);
            player.Position = new WorldPosition(found.Position.X - distances[index], 0f, found.Position.Z);
            player.CurrentHealth = Spare;
        }

        server.Tick();
        return (server, players, found);
    }

    private static void Cast(TestServer server, MonsterEntity caster, SkillDefinitionId skill, PlayerEntity target)
    {
        MapInstance map = QuarryOf(server);
        Assert.That(server.Combat.CanMonsterCast(map, caster, skill, target, server.CurrentTick), Is.True);
        server.Combat.BeginMonsterCast(map, caster, skill, target, server.CurrentTick);
    }

    // What a monster's skill takes from a player wearing the cards, every roll at its least, with the AI off.
    private static int Taken(string monster, SkillDefinitionId skill, Func<TestServer, CardBonus> cards)
    {
        (TestServer server, ConnectionId[] players, MonsterEntity caster) = Beside(monster, new Draws(0), false, 1.2f);
        PlayerEntity player = server.PlayerOf(players[0]);
        player.CardBonus = cards(server);
        Cast(server, caster, skill, player);
        server.Tick(TestServer.TickRate);
        return Spare - player.CurrentHealth;
    }

    // What the player's first swing, or Arcane Bolt, takes from the first monster of a kind, with the AI off.
    private static int FirstHitOn(
        string monster,
        Func<TestServer, CardBonus>? cards = null,
        string? converter = null,
        Element converted = Element.Neutral,
        bool isBolt = false)
    {
        (TestServer server, ConnectionId[] players, MonsterEntity target) = Beside(monster, new Draws(0), false, 1.2f);
        PlayerEntity player = server.PlayerOf(players[0]);
        // A weapon's part of 38, which a card's percentage visibly grows (Gameplay Systems §11.7).
        player.Equipment = new WornEquipment(38, 0, 0, 0);
        player.CardBonus = cards?.Invoke(server) ?? CardBonus.None;
        if (converter != null)
        {
            player.StartStatusEffect(
                new ActiveStatusEffect(new StatusDefinitionId(converter), default, long.MaxValue, converted));
        }

        if (isBolt)
        {
            player.SetSkillLevel(new SkillDefinitionId("skill.arcane_bolt"), 1);
            player.CurrentSpirit = player.MaxSpirit;
        }

        server.Tick();
        int before = target.CurrentHealth;
        if (isBolt)
        {
            server.SendUseSkill(players[0], "skill.arcane_bolt", target.Id, 1);
        }
        else
        {
            server.SendAttack(players[0], target.Id, 1);
        }

        // A bolt's cast and flight take longer than a swing; either lands within its five seconds.
        for (int tick = 0; tick < TestServer.TickRate * 5 && target.CurrentHealth == before; tick++)
        {
            server.Tick();
        }

        Assert.That(target.CurrentHealth, Is.LessThan(before), $"{monster}: struck");
        return before - target.CurrentHealth;
    }

    // Gameplay Systems §10: when a Basalt Crawler begins a swing, its idle kin within 6 m that see it take its target;
    // one farther off, and the other pairs, stay idle.
    [TestCase(5.9f, true)]
    [TestCase(6.1f, false)]
    public void ASwing_OfABasaltCrawler_BringsItsKinWithinSixMetres_AndNoOtherPair(float distance, bool isJoined)
    {
        var server = new TestServer(
            withEveryMap: true,
            withQuarry: true,
            withMonsters: true,
            combatRandom: new Draws(9999));
        ConnectionId connection = server.EnterWorld(1);
        server.CrossIntoTheQuarry(connection);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentHealth = Spare;
        MonsterEntity[] basalts = AllOf(server, BasaltCrawler);
        Assert.That(basalts, Has.Length.EqualTo(6));
        Assert.That(basalts.Select(basalt => basalt.Brain.State), Is.All.EqualTo(MonsterAiState.Idle));
        var caller = new WorldPosition(32f, 0f, -30f);
        basalts[0].Position = caller;
        basalts[1].Position = new WorldPosition(caller.X, 0f, caller.Z + distance);
        player.Position = new WorldPosition(caller.X - 1.2f, 0f, caller.Z);
        basalts[0].Target = player.Id;
        basalts[0].Combat.IsAutoAttacking = true;
        basalts[0].Brain.State = MonsterAiState.Chase;

        server.Tick(2);

        Assert.That(basalts[0].Combat.IsSwinging, Is.True, "the caller swings");
        Assert.That(basalts[1].Target, Is.EqualTo(isJoined ? player.Id : default));
        Assert.That(basalts.Skip(2).Select(basalt => basalt.Target), Is.All.EqualTo(default(EntityId)));
    }

    // Gameplay Systems §9, §10: each of the boss's casts takes 2 s, then holds its next swing a second.
    [TestCase("skill.furnace_burst")]
    [TestCase("skill.ash_cloud")]
    public void TheBossesCast_TakesTwoSeconds_ThenHoldsItsNextSwingASecond(string skill)
    {
        (TestServer server, ConnectionId[] players, MonsterEntity boss) =
            Beside(FurnaceHeart, new Draws(9999), false, 1.5f);
        PlayerEntity player = server.PlayerOf(players[0]);

        Cast(server, boss, new SkillDefinitionId(skill), player);
        long began = boss.Combat.CastBeganMs;
        long resolves = boss.Combat.CastResolveMs;
        boss.Target = player.Id;
        boss.Combat.IsAutoAttacking = true;
        uint? ended = null;
        uint? swung = null;
        for (int tick = 0; tick < TestServer.TickRate * 5 && swung == null; tick++)
        {
            server.Tick();
            if (ended == null && !boss.Combat.IsCasting)
            {
                ended = server.CurrentTick;
            }

            if (boss.Combat.IsSwinging)
            {
                swung = boss.Combat.SwingStartTick;
            }
        }

        Assert.That(resolves - began, Is.EqualTo(2000), "a two-second cast");
        Assert.That((ended, swung), Is.Not.EqualTo((default(uint?), default(uint?))));
        Assert.That(
            (swung!.Value - ended!.Value) * 1000 / TestServer.TickRate,
            Is.InRange(1000, 1000 + 1000 / TestServer.TickRate),
            "a second's delay before the next swing");
    }

    // Every roll at its least but for the ailments' draw, which the test names: dodges and criticals draw 999, the hit
    // roll and the damage 0.
    private sealed class Draws : IRandomSource
    {
        private readonly int m_ailment;

        public Draws(int ailment)
        {
            m_ailment = ailment;
        }

        public int Next(int exclusiveMax)
        {
            return exclusiveMax switch
            {
                1000 => 999,
                10_000 => m_ailment,
                _ => 0
            };
        }
    }

    // Gameplay Systems §3, §11.7: on the Fire crawler a swing grows with the Dousing card and with a Water weapon and
    // shrinks with a Fire one; the card adds nothing to a spell, nor to a swing on the Wind wisp.
    [Test]
    public void ASwing_OnTheFireCrawler_GrowsWithTheDousingCardAndWater_AndNeitherASpellNorAWindBodyTakesTheCard()
    {
        Func<TestServer, CardBonus> dousing = server => CardOf(server, "item.card.ash_wisp");

        int plain = FirstHitOn(CinderCrawler);
        int doused = FirstHitOn(CinderCrawler, dousing);
        int water = FirstHitOn(CinderCrawler, converter: "status.water_weapon", converted: Element.Water);
        int fire = FirstHitOn(CinderCrawler, converter: "status.fire_weapon", converted: Element.Fire);
        int bolt = FirstHitOn(CinderCrawler, isBolt: true);
        int dousedBolt = FirstHitOn(CinderCrawler, dousing, isBolt: true);
        int wisp = FirstHitOn(AshWisp);
        int dousedWisp = FirstHitOn(AshWisp, dousing);

        Assert.That(doused, Is.GreaterThan(plain), "the Dousing card on a Fire body");
        Assert.That((water > plain, fire < plain), Is.EqualTo((true, true)), "Water on Fire, and Fire on Fire");
        Assert.That(dousedBolt, Is.EqualTo(bolt), "a spell takes nothing of the card");
        Assert.That(dousedWisp, Is.EqualTo(wisp), "nor a swing on a Wind body");
    }

    // Gameplay Systems §10: a player within its 3.5 m sends the Ash Wisp walking away to strike from its range.
    [Test]
    public void AnAshWisp_WithAPlayerTooClose_WalksAwayToItsRange()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity wisp) = Beside(AshWisp, new Draws(9999), true, 1f);
        PlayerEntity player = server.PlayerOf(players[0]);
        wisp.Brain.LastAttacker = player.Id;
        server.Tick(3);
        Assert.That(wisp.Target, Is.EqualTo(player.Id), "the wisp turned on the player");

        server.Tick(TestServer.TickRate * 2);

        Assert.That(Distance(wisp.Position, player.Position), Is.GreaterThanOrEqualTo(3.5f));
    }

    // Gameplay Systems §9.1: Hush silences the player it strikes for its 15 s less the INT's resistance, and a silenced
    // player casts nothing.
    [Test]
    public void AnAshWispsHush_SilencesThePlayer()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity wisp) = Beside(AshWisp, new Draws(0), false, 4f);
        PlayerEntity player = server.PlayerOf(players[0]);

        Cast(server, wisp, Hush, player);
        server.Tick(TestServer.TickRate);

        Assert.That(player.HasAilment(Ailment.Silence), Is.True);
    }

    // Gameplay Systems §9.1: Ash Cloud blinds every player within 6 m and in the boss's sight when its 2 s cast ends.
    [Test]
    public void AshCloud_BlindsEveryPlayerWithinSixMetres()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity boss) =
            Beside(FurnaceHeart, new Draws(0), false, 2f, 5.9f, 6.1f);
        PlayerEntity[] entities = players.Select(server.PlayerOf).ToArray();

        Cast(server, boss, AshCloud, entities[0]);
        server.Tick(TestServer.TickRate * 2 + 1);

        Assert.That(
            entities.Select(player => player.HasAilment(Ailment.Blind)),
            Is.EqualTo(new[] { true, true, false }));
    }

    // Gameplay Systems §9.1: Basalt Bash stuns at its 20 % less what the player's VIT resists, the Steady card's 20 %
    // lowering it further; a draw between the two thresholds stuns a bare player and spares a Steady one.
    [Test]
    public void BasaltBash_StunsAtItsChanceLessTheVitsResistance_AndTheSteadyCardResistsMore()
    {
        (TestServer probe, ConnectionId[] probed, MonsterEntity basalt) =
            Beside(BasaltCrawler, new Draws(0), false, 1.2f);
        PlayerEntity player = probe.PlayerOf(probed[0]);
        var rules = new AilmentRules();
        int resistance = rules.PlayerResistance(
            player.EffectivePrimary.Vit,
            rules.LevelAdvantage(basalt.Definition.Level, player.Level));
        int bare = rules.LandingChance(2000, 1, resistance, 0);
        int steady = rules.LandingChance(2000, 1, resistance, rules.CardResistance(20));

        bool[] stunned = new[]
                { (bare - 1, false), (bare, false), (steady - 1, true), (steady, true), (bare - 1, true) }
            .Select(trial =>
            {
                (TestServer server, ConnectionId[] players, MonsterEntity caster) =
                    Beside(BasaltCrawler, new Draws(trial.Item1), false, 1.2f);
                PlayerEntity target = server.PlayerOf(players[0]);
                target.CardBonus = trial.Item2 ? CardOf(server, "item.card.basalt_crawler") : CardBonus.None;
                Cast(server, caster, BasaltBash, target);
                server.Tick();
                return target.HasAilment(Ailment.Stun);
            })
            .ToArray();

        Assert.That(steady, Is.LessThan(bare));
        Assert.That(stunned, Is.EqualTo(new[] { true, false, true, false, false }));
    }

    // Gameplay Systems §3, §9: Cinder Bite is Fire, so it meets the armor's element after the defense, then the cards'
    // reduction of Fire: the Furnace Heart Card's Fire armor takes a quarter of it, the Fireward card a fifth off.
    [Test]
    public void CinderBite_IsFire_SoFireArmorTakesAQuarterOfIt_AndTheFirewardCardAFifthOff()
    {
        int bare = Taken(CinderCrawler, CinderBite, _ => CardBonus.None);
        int molten = Taken(CinderCrawler, CinderBite, server => CardOf(server, "item.card.furnace_heart"));
        int fireward = Taken(CinderCrawler, CinderBite, server => CardOf(server, "item.card.cinder_crawler"));

        Assert.That(bare, Is.GreaterThan(4));
        Assert.That(
            (molten, fireward),
            Is.EqualTo((bare * 25 / 100, bare * 80 / 100)),
            "Fire on Fire armor a quarter; the Fireward card four fifths");
    }

    // Gameplay Systems §9: Furnace Burst strikes every player within 5 m and in the boss's sight when its 2 s cast
    // ends; a player who stepped out before then is spared, and one beyond is never struck.
    [Test]
    public void FurnaceBurst_StrikesEveryPlayerWithinFiveMetres_AndSparesOneWhoStepsOut()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity boss) =
            Beside(FurnaceHeart, new Draws(9999), false, 2f, 4.9f, 5.1f, 3f);
        PlayerEntity[] entities = players.Select(server.PlayerOf).ToArray();

        Cast(server, boss, FurnaceBurst, entities[0]);
        server.Tick(TestServer.TickRate);
        entities[3].Position = new WorldPosition(boss.Position.X - 5.5f, 0f, boss.Position.Z);
        server.Tick(TestServer.TickRate + 1);

        Assert.That(
            entities.Select(player => player.CurrentHealth < Spare),
            Is.EqualTo(new[] { true, true, false, false }));
    }

    // Gameplay Systems §10: the boss's 18 m leash reaches past every corner of its arena, so a fight there never sends
    // it home; pulled down the road below, it walks home and recovers all its HP.
    [Test]
    public void TheFurnaceHeart_NeverLeashesInItsArena_AndHealsOnlyWhenPulledHome()
    {
        var server = new TestServer(withEveryMap: true, withQuarry: true, withMonsters: true);
        ConnectionId connection = server.EnterWorld(1);
        server.CrossIntoTheQuarry(connection);
        PlayerEntity player = server.PlayerOf(connection);
        MonsterEntity boss = AllOf(server, FurnaceHeart).Single();
        player.CurrentHealth = Spare;
        player.Position = new WorldPosition(boss.Home.X, 0f, boss.Home.Z - 5f);
        boss.CurrentHealth -= 5000;
        server.Tick(TestServer.TickRate);
        bool wasNoticed = boss.Target == player.Id;

        player.Position = new WorldPosition(-11.5f, 0f, 16.5f);
        bool hasLeashedInTheArena = false;
        for (int tick = 0; tick < 10 * TestServer.TickRate; tick++)
        {
            server.Tick();
            hasLeashedInTheArena |= boss.Brain.State == MonsterAiState.ReturnHome;
        }

        int woundsInTheArena = boss.MaxHealth - boss.CurrentHealth;
        player.Position = new WorldPosition(0f, 0f, 8f);
        bool hasLeashed = false;
        bool isHome = false;
        for (int tick = 0; tick < 60 * TestServer.TickRate && !isHome; tick++)
        {
            server.Tick();
            hasLeashed |= boss.Brain.State == MonsterAiState.ReturnHome;
            isHome = hasLeashed && boss.Brain.State == MonsterAiState.Idle;
        }

        Assert.That(wasNoticed, Is.True, "the aggressive boss took the player within its 6 m");
        Assert.That((hasLeashedInTheArena, woundsInTheArena >= 5000), Is.EqualTo((false, true)), "no reset inside");
        Assert.That((hasLeashed, isHome), Is.EqualTo((true, true)), "it walked home from the road");
        Assert.That(boss.CurrentHealth, Is.EqualTo(boss.MaxHealth));
    }

    // Gameplay Systems §9.1: a boss suffers no ailment; a card that would bring every one of them on a hit brings none
    // on the Furnace Heart, where it stuns a Cinder Crawler.
    [Test]
    public void TheFurnaceHeart_SuffersNoAilment()
    {
        var everything = new CardBonus(
            new PrimaryStats(0, 0, 0, 0, 0, 0),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            inflict: Enum.GetValues(typeof(Ailment))
                .Cast<Ailment>()
                .Where(ailment => ailment != Ailment.None)
                .ToDictionary(ailment => ailment, _ => 10_000));
        bool[] afflicted = new[] { FurnaceHeart, CinderCrawler }
            .Select(monster =>
            {
                (TestServer server, ConnectionId[] players, MonsterEntity target) =
                    Beside(monster, new Draws(0), false, 1.2f);
                server.PlayerOf(players[0]).CardBonus = everything;
                int before = target.CurrentHealth;
                server.SendAttack(players[0], target.Id, 1);
                server.TickUntil(() => target.CurrentHealth < before);
                return Enum.GetValues(typeof(Ailment))
                    .Cast<Ailment>()
                    .Where(ailment => ailment != Ailment.None)
                    .Any(target.HasAilment);
            })
            .ToArray();

        Assert.That(afflicted, Is.EqualTo(new[] { false, true }));
    }
}
}
