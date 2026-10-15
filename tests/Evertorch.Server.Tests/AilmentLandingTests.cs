using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Landing an ailment (Gameplay Systems §9.1): one draw from the combat source per source, made before the
///     refusal, the immunity, and the resistance; an ailment skill's Applied or Resisted; a worn card's chance on a
///     physical hit; the break a landed hit makes of a freeze or a sleep, which then starts no hold; a held body always
///     hit; a frozen one Water; the option's scale; the sheet's resistances; and the party's ailments.
/// </summary>
[TestFixture]
public sealed class AilmentLandingTests
{
    private const int ChanceRange = 10_000;
    private static readonly StatusDefinitionId Poisoned = new("status.poisoned");
    private static readonly StatusDefinitionId Stunned = new("status.stunned");
    private static readonly StatusDefinitionId Frozen = new("status.frozen");
    private static readonly StatusDefinitionId Silenced = new("status.silenced");
    private static readonly StatusDefinitionId WindWeapon = new("status.wind_weapon");
    private static readonly StatusDefinitionId FireWeapon = new("status.fire_weapon");
    private static readonly SkillDefinitionId NumbingSpark = new("skill.numbing_spark");
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");

    private static CardBonus Inflicting(Ailment ailment, int basisPoints)
    {
        return new CardBonus(new PrimaryStats(0, 0, 0, 0, 0, 0), 0, 0, 0, 0, 0, 0, 0,
            inflict: new Dictionary<Ailment, int> { [ailment] = basisPoints });
    }

    // The Gloom Wisp's Numbing Spark as a pure silence at the chance given, its statistics left alone.
    private static ServerContent WithAHush(string chance)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, "skills.json", NumbingSpark.Value, "levels[0].effect.status",
            "\"status.silenced\"");
        PackageFixture.SetValue(files, "skills.json", NumbingSpark.Value, "levels[0].effect.statPercent",
            "{\"str\": 0, \"agi\": 0, \"vit\": 0, \"int\": 0, \"dex\": 0, \"luk\": 0}");
        PackageFixture.SetValue(files, "skills.json", NumbingSpark.Value, "levels[0].effect.chance", chance);
        return ServerContentLoader.Load(files);
    }

    private static (TestServer Server, ConnectionId Player, MonsterEntity Wisp, MapInstance Map) InTheGallery(
        ServerContent content,
        int scale = 1,
        IRandomSource? random = null)
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false,
            content: content, ailmentChanceScale: scale, combatRandom: random ?? new SureHitRandom());
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        server.PlayerOf(player).Position = new WorldPosition(12f, 0f, -19.5f);
        server.World.TryGetMap(Grotto, out MapInstance? grotto);
        MonsterEntity wisp = grotto!.Monsters.First(monster => monster.Definition.Id.Value == "monster.gloom_wisp");
        wisp.Position = new WorldPosition(16f, 0f, -19.5f);
        server.Tick();
        return (server, player, wisp, grotto);
    }

    private static SkillResolved[] ResolvedTo(TestServer server, ConnectionId connection)
    {
        return server.Transport.SentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.SkillResolved)
            .Select(message => SkillResolved.TryRead(message.Payload, out SkillResolved? read) ? read! : null!)
            .ToArray();
    }

    private static void Cast(TestServer server, MapInstance map, MonsterEntity wisp, PlayerEntity player)
    {
        Assert.That(server.Combat.CanMonsterCast(map, wisp, NumbingSpark, player, server.CurrentTick), Is.True);
        server.Combat.BeginMonsterCast(map, wisp, NumbingSpark, player, server.CurrentTick);
        server.Tick(TestServer.TickRate);
    }

    private static AilmentRolls RollsOf(TestServer server, IRandomSource random)
    {
        return new AilmentRolls(server.Content, server.StatusEffects, server.World, random, server.Instruments, 1);
    }

    // Always hits but for its roll: dodges and criticals draw 999, the hit roll 99, an ailment's roll the scripted
    // draw, everything else 0.
    private sealed class AilmentDrawRandom : IRandomSource
    {
        private readonly int m_draw;

        public AilmentDrawRandom(int draw)
        {
            m_draw = draw;
        }

        public int Next(int exclusiveMax)
        {
            return exclusiveMax switch
            {
                1000 => 999,
                ChanceRange => m_draw,
                _ => 0
            };
        }
    }

    // Misses every roll that can miss: the hit roll draws 99, dodges and criticals 999.
    private sealed class MissingRandom : IRandomSource
    {
        public int Next(int exclusiveMax)
        {
            return exclusiveMax switch
            {
                1000 => 999,
                100 => 99,
                _ => 0
            };
        }
    }

    [Test]
    public void AilmentChanceScale_RaisesTheChance_BeforeResistance()
    {
        (TestServer server, ConnectionId player, MonsterEntity wisp, MapInstance map) =
            InTheGallery(WithAHush("0.2"), 5);

        Cast(server, map, wisp, server.PlayerOf(player));

        Assert.That(server.PlayerOf(player).HasAilment(Ailment.Silence), Is.True, "2,000 × 5 is certain");
    }

    [Test]
    public void AilmentSkill_ThatLands_AnswersApplied_AndOneThatDoesNot_Resisted_BothWithNoAmount()
    {
        (TestServer server, ConnectionId player, MonsterEntity wisp, MapInstance map) = InTheGallery(WithAHush("1"));
        PlayerEntity entity = server.PlayerOf(player);
        server.Transport.ClearSent();
        Cast(server, map, wisp, entity);
        SkillResolved landed = ResolvedTo(server, player).Single();
        bool isSilenced = entity.HasAilment(Ailment.Silence);
        long lasting = entity.StatusEffects.Single().EndMs - server.World.NowMs;

        (TestServer other, ConnectionId otherPlayer, MonsterEntity otherWisp, MapInstance otherMap) =
            InTheGallery(WithAHush("0.5"));
        other.Transport.ClearSent();
        Cast(other, otherMap, otherWisp, other.PlayerOf(otherPlayer));
        SkillResolved resisted = ResolvedTo(other, otherPlayer).Single();

        Assert.That((landed.Outcome, landed.Amount, isSilenced), Is.EqualTo((SkillOutcome.Applied, 0u, true)));
        Assert.That(lasting, Is.InRange(14_000, 15_000), "INT 5 against a level-16 source resists nothing");
        Assert.That((resisted.Outcome, resisted.Amount), Is.EqualTo((SkillOutcome.Resisted, 0u)),
            "5,000 in 10,000, the draw 9,999");
        Assert.That(other.PlayerOf(otherPlayer).StatusEffects, Is.Empty);
    }

    [Test]
    public void FrozenTarget_IsWaterAtTheHit()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false,
            combatRandom: new SureHitRandom());
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, 1);
        server.Store.GiveItems(1, "item.weapon.training_sword", 1, 1, 0);
        server.Store.Wear(1, server.Store.Stored(1).Items.Last().Id, "Weapon");
        server.SendEnterWorld(connection, 1);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        server.CrossIntoTheGrotto(connection);
        PlayerEntity player = server.PlayerOf(connection);
        server.World.TryGetMap(Grotto, out MapInstance? grotto);
        MonsterEntity crawler =
            grotto!.Monsters.First(monster => monster.Definition.Id.Value == "monster.grotto_crawler");
        player.Position = new WorldPosition(crawler.Position.X + 1f, crawler.Position.Y, crawler.Position.Z);
        server.Tick(2);
        uint sequence = 0;

        int Swing()
        {
            int before = crawler.CurrentHealth;
            server.SendAttack(connection, crawler.Id, ++sequence);
            for (int tick = 0; tick < 200 && crawler.CurrentHealth == before; tick++)
            {
                server.Tick();
            }

            server.SendCancel(connection, ++sequence);
            server.Tick(TestServer.TickRate);
            int dealt = before - crawler.CurrentHealth;
            crawler.CurrentHealth = crawler.MaxHealth;
            return dealt;
        }

        int Struck(StatusDefinitionId converter, bool isFrozen)
        {
            server.StatusEffects.Apply(player, converter, default, server.World.NowMs + 600_000);
            if (isFrozen)
            {
                server.StatusEffects.Apply(crawler, Frozen, default, server.World.NowMs + 600_000);
            }

            return Swing();
        }

        (int Wind, int Fire) earth = (Struck(WindWeapon, false), Struck(FireWeapon, false));
        (int Wind, int Fire) water = (Struck(WindWeapon, true), Struck(FireWeapon, true));

        Assert.That(earth.Fire, Is.GreaterThan(earth.Wind), "Fire beats Earth, where Wind does not");
        Assert.That(water.Wind, Is.GreaterThan(water.Fire), "frozen, the crawler is Water, which Wind beats");
        Assert.That(crawler.HasAilment(Ailment.Freeze), Is.False, "the hit thawed it");
    }

    [Test]
    public void HeldTarget_IsAlwaysHit()
    {
        var rig = new CombatRig(combatRandom: new MissingRandom());
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        rig.Server.Tick(TestServer.TickRate);
        CombatResult free = rig.Damages().Last().Result;
        rig.Server.StatusEffects.Apply(rig.Slime, Stunned, default, rig.Server.World.NowMs + 60_000);
        rig.TickUntilStarts(rig.Starts().Count + 1);
        rig.Server.Tick(TestServer.TickRate);

        Assert.That(free, Is.EqualTo(CombatResult.Miss));
        Assert.That(rig.Damages().Last().Result, Is.EqualTo(CombatResult.Hit), "the roll still drew 99");
    }

    [Test]
    public void LandedHit_BreaksAFreeze_AndTheSameHitStartsNoHold()
    {
        var rig = new CombatRig(combatRandom: new AilmentDrawRandom(0));
        rig.Server.StatusEffects.Apply(rig.Slime, Frozen, default, rig.Server.World.NowMs + 60_000);
        rig.Entity.CardBonus = Inflicting(Ailment.Stun, ChanceRange);
        rig.Attack(rig.Slime.Id);
        rig.Server.TickUntil(() => rig.Damages().Count == 1);
        (bool Frozen, bool Stunned) afterTheBreak =
            (rig.Slime.HasAilment(Ailment.Freeze), rig.Slime.HasAilment(Ailment.Stun));
        rig.Server.TickUntil(() => rig.Damages().Count == 2);

        Assert.That(afterTheBreak, Is.EqualTo((false, false)), "the hit thawed it and stunned nothing");
        Assert.That(rig.Slime.HasAilment(Ailment.Stun), Is.True, "the next hit stuns it");
    }

    [Test]
    public void Party_HearsAMembersAilments()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, "Tester7", eight, "Tester8");
        rig.Server.Tick(TestServer.TickRate);
        PlayerEntity player = rig.Server.PlayerOf(eight);
        rig.Server.StatusEffects.Apply(player, Poisoned, default, rig.Server.World.NowMs + 60_000);
        rig.Server.StatusEffects.Apply(player, Silenced, default, rig.Server.World.NowMs + 60_000);

        rig.Server.Tick(2 * TestServer.TickRate);

        PartyMemberStatus heard = rig.Server.Transport.ControlSentTo(seven)
            .Where(message => message.Opcode == MessageOpcode.PartyMemberStatus)
            .Select(message =>
                PartyMemberStatus.TryRead(message.Payload, out PartyMemberStatus? status) ? status! : null!)
            .Last(status => status.Name == "Tester8");
        Assert.That(heard.Ailments, Is.EqualTo((byte)(0x01 | 0x10)), "poison bit 0, silence bit 4");
    }

    [Test]
    public void Roll_IsRefusedForTheSameAilmentOrASecondHold_ImmuneForABossOrAnUndeadFreeze_AndAlwaysDraws()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false);
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        server.World.TryGetMap(Grotto, out MapInstance? grotto);
        MonsterEntity boss = grotto!.Monsters.First(monster => monster.Definition.IsBoss);
        MonsterEntity undead =
            grotto.Monsters.First(monster => monster.Definition.Id.Value == "monster.barrow_crawler");
        MonsterEntity crawler =
            grotto.Monsters.First(monster => monster.Definition.Id.Value == "monster.grotto_crawler");
        var random = new ScriptedRandom(0);
        AilmentRolls rolls = RollsOf(server, random);

        AilmentOutcome first = rolls.Roll(player, crawler, Poisoned, ChanceRange, 20_000);
        AilmentOutcome again = rolls.Roll(player, crawler, Poisoned, ChanceRange, 20_000);
        AilmentOutcome stun = rolls.Roll(player, crawler, Stunned, ChanceRange, 4000);
        AilmentOutcome secondHold = rolls.Roll(player, crawler, Frozen, ChanceRange, 8000);
        AilmentOutcome bossPoison = rolls.Roll(player, boss, Poisoned, ChanceRange, 20_000);
        AilmentOutcome undeadFreeze = rolls.Roll(player, undead, Frozen, ChanceRange, 8000);
        AilmentOutcome undeadPoison = rolls.Roll(player, undead, Poisoned, ChanceRange, 20_000);
        AilmentOutcome broken = rolls.Roll(player, undead, Stunned, ChanceRange, 4000, true);

        Assert.That(
            new[] { first, again, stun, secondHold, bossPoison, undeadFreeze, undeadPoison, broken },
            Is.EqualTo(new[]
            {
                AilmentOutcome.Landed, AilmentOutcome.Refused, AilmentOutcome.Landed, AilmentOutcome.Refused,
                AilmentOutcome.Immune, AilmentOutcome.Immune, AilmentOutcome.Landed, AilmentOutcome.Refused
            }));
        Assert.That(random.Bounds, Is.EqualTo(Enumerable.Repeat(ChanceRange, 8)), "each source draws once");
        Assert.That(boss.StatusEffects, Is.Empty);
    }

    [Test]
    public void Roll_OnAPlayer_IsResistedByTheStatisticTheCardsAndTheLevelGap()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false);
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        player.SetPrimary(new PrimaryStats(5, 5, 30, 5, 5, 5));
        player.CardBonus = new CardBonus(new PrimaryStats(0, 0, 0, 0, 0, 0), 0, 0, 0, 0, 0, 0, 0,
            resist: new Dictionary<Ailment, int> { [Ailment.Stun] = 30 });
        server.Stats.Recalculate(player, server.Content.Jobs[player.Job]);
        MonsterEntity slime = server.World.Maps.SelectMany(map => map.Monsters)
            .First(monster => monster.Definition.Id.Value == "monster.training_slime");
        MonsterEntity crawler = server.World.Maps.SelectMany(map => map.Monsters)
            .First(monster => monster.Definition.Id.Value == "monster.grotto_crawler");

        // Crushing Bite's 1,500 against VIT 30 and Earthen armor, from no higher a level: 735 in 10,000.
        AilmentOutcome resisted = RollsOf(server, new ScriptedRandom(735)).Roll(slime, player, Stunned, 1500, 4000);
        AilmentOutcome landed = RollsOf(server, new ScriptedRandom(734)).Roll(slime, player, Stunned, 1500, 4000);
        long lasting = player.StatusEffects.Single().EndMs - server.World.NowMs;
        server.StatusEffects.EndAll(player);

        // A level-14 source against level 1: an advantage of 33 leaves VIT 30 nothing.
        AilmentOutcome gapped = RollsOf(server, new ScriptedRandom(5099)).Roll(crawler, player, Stunned, 1500, 4000);

        Assert.That((resisted, landed, lasting), Is.EqualTo((AilmentOutcome.Resisted, AilmentOutcome.Landed, 1960L)));
        Assert.That(gapped, Is.EqualTo(AilmentOutcome.Resisted), "1,500 × 70 % = 1,050 by the card alone");
        Assert.That(RollsOf(server, new ScriptedRandom(1049)).Roll(crawler, player, Stunned, 1500, 4000),
            Is.EqualTo(AilmentOutcome.Landed));
    }

    [Test]
    public void Sheet_GivesTheResistances_AgainstASourceOfTheSameLevel_WithTheCards()
    {
        var server = new TestServer();
        PlayerEntity player = server.PlayerOf(server.EnterWorld(7));
        player.SetPrimary(new PrimaryStats(5, 12, 30, 40, 5, 5));
        player.CardBonus = new CardBonus(new PrimaryStats(0, 0, 0, 0, 0, 0), 0, 0, 0, 0, 0, 0, 0,
            resist: new Dictionary<Ailment, int> { [Ailment.Stun] = 30, [Ailment.Freeze] = 50 });
        server.Stats.Recalculate(player, server.Content.Jobs[player.Job]);

        CharacterSheet sheet = server.Builds.SheetOf(player);
        int softMagicDefense = player.Stats.SoftMagicDefense;

        Assert.That(
            (sheet.PoisonResistance, sheet.StunResistance, sheet.SleepResistance, sheet.SilenceResistance,
                sheet.BlindResistance),
            Is.EqualTo(((byte)30, (byte)51, (byte)12, (byte)40, (byte)40)));
        Assert.That(sheet.FreezeResistance, Is.EqualTo((byte)((10_000 - (100 - softMagicDefense) * 50) / 100)));
    }

    [Test]
    public void Swing_WithNoCardAilment_DrawsNoAilmentRoll()
    {
        var random = new ScriptedRandom(0);
        var rig = new CombatRig(combatRandom: random);
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(2);

        Assert.That(random.Bounds, Has.None.EqualTo(ChanceRange));
    }

    [Test]
    public void WornCard_OnALandedSwing_PoisonsTheSlime_ForItsStatusesTimeLessItsLevel()
    {
        var rig = new CombatRig(combatRandom: new AilmentDrawRandom(0));
        rig.Entity.CardBonus = Inflicting(Ailment.Poison, ChanceRange);
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        rig.Server.Tick(TestServer.TickRate);

        Assert.That(rig.Slime.HasAilment(Ailment.Poison), Is.True);
        Assert.That(rig.Slime.StatusEffects.Single().EndMs - rig.Server.World.NowMs, Is.InRange(18_000, 19_800),
            "20,000 ms less 1 % for the slime's level");
    }
}
}
