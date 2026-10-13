using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Barrow Crawler's Grave Bite (Gameplay Systems §9; Milestone 18 decision 16): a Shadow physical bite with no
///     cast time, so it lands in the tick after it starts and shows no cast bar; it rolls to hit as a swing does, so it
///     misses or is perfectly dodged, and its Shadow meets the cards' reduction of Shadow.
/// </summary>
[TestFixture]
public sealed class GraveBiteTests
{
    private const int Spare = 1_000_000;

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly SkillDefinitionId GraveBite = new("skill.grave_bite");

    // A player of AGI 99, so flee 200 and the crawler's hit of 238 lands 38 times in 100, a step west of a Barrow
    // Crawler at its spawn's center; the AI is off, so only the test bites.
    private static (TestServer Server, ConnectionId Player, MonsterEntity Crawler) BesideTheBarrowPack(
        IRandomSource random,
        CardBonus? cards = null)
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            combatRandom: random);
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        server.World.TryGetMap(Grotto, out MapInstance? map);
        MonsterEntity crawler = map!.Monsters.First(monster => monster.Definition.Id.Value == "monster.barrow_crawler");
        crawler.Position = new WorldPosition(-20.5f, 0f, -26.5f);
        server.Place(player, -21.5f, -26.5f);
        PlayerEntity entity = server.PlayerOf(player);
        entity.SetPrimary(new PrimaryStats(5, 99, 5, 5, 5, 5));
        server.Stats.Recalculate(entity, server.Content.Jobs[entity.Job]);
        entity.CurrentHealth = Spare;
        entity.CardBonus = cards ?? CardBonus.None;
        server.Tick();
        server.Transport.ClearSent();
        return (server, player, crawler);
    }

    private static void Bite(TestServer server, ConnectionId player, MonsterEntity crawler)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(server.Combat.CanMonsterCast(map!, crawler, GraveBite, entity, server.CurrentTick), Is.True);
        server.Combat.BeginMonsterCast(map!, crawler, GraveBite, entity, server.CurrentTick);
        server.Tick();
    }

    private static SkillResolved ResolvedTo(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.SkillResolved)
            .Select(message => SkillResolved.TryRead(message.Payload, out SkillResolved? resolved) ? resolved! : null!)
            .Single();
    }

    [Test]
    public void GraveBite_IsPerfectlyDodged_WhenThePerfectDodgeRollLands()
    {
        var random = new ScriptedRandom(0, 14);
        (TestServer server, ConnectionId player, MonsterEntity crawler) = BesideTheBarrowPack(random);
        int draws = random.Calls;

        Bite(server, player, crawler);

        SkillResolved resolved = ResolvedTo(server, player);
        Assert.That((resolved.Outcome, resolved.Amount), Is.EqualTo((SkillOutcome.Miss, 0u)));
        Assert.That(random.Bounds.Skip(draws), Is.EqualTo(new[] { 1000 }), "the dodge ends the roll at its first draw");
        Assert.That(server.PlayerOf(player).CurrentHealth, Is.EqualTo(Spare));
    }

    // Gameplay Systems §3, §11.7: the Shrouded card's Shadow -30 % takes three tenths of the bite.
    [Test]
    public void GraveBite_IsShadow_SoTheCardsReductionOfShadowCutsIt()
    {
        (TestServer bare, ConnectionId first, MonsterEntity firstCrawler) = BesideTheBarrowPack(new SureHitRandom());
        (TestServer shrouded, ConnectionId second, MonsterEntity secondCrawler) = BesideTheBarrowPack(
            new SureHitRandom(),
            new CardBonus(
                new PrimaryStats(0, 0, 0, 0, 0, 0),
                0,
                0,
                0,
                30,
                0,
                0,
                0,
                elementDamageReduction: new Dictionary<Element, int> { [Element.Shadow] = 30 }));

        Bite(bare, first, firstCrawler);
        Bite(shrouded, second, secondCrawler);

        int full = Spare - bare.PlayerOf(first).CurrentHealth;
        Assert.That(full, Is.Positive);
        Assert.That(Spare - shrouded.PlayerOf(second).CurrentHealth, Is.EqualTo(full * 70 / 100));
    }

    // The perfect dodge, LUK 5 + 10 = 15 in 1,000, draws first; then the critical, which a skill never lands; then the
    // hit, 38 in 100.
    [Test]
    public void GraveBite_Misses_WhenItsHitRollFails()
    {
        var random = new ScriptedRandom(0, 999, 999, 99);
        (TestServer server, ConnectionId player, MonsterEntity crawler) = BesideTheBarrowPack(random);
        int draws = random.Calls;

        Bite(server, player, crawler);

        SkillResolved resolved = ResolvedTo(server, player);
        Assert.That((resolved.Outcome, resolved.Amount), Is.EqualTo((SkillOutcome.Miss, 0u)));
        Assert.That(random.Bounds.Skip(draws), Is.EqualTo(new[] { 1000, 1000, 100 }), "all three rolls drawn");
        Assert.That(server.PlayerOf(player).CurrentHealth, Is.EqualTo(Spare));
    }

    [Test]
    public void GraveBite_StartsWithNoCastTime_AndLandsInTheNextTick()
    {
        (TestServer server, ConnectionId player, MonsterEntity crawler) = BesideTheBarrowPack(new SureHitRandom());

        Bite(server, player, crawler);

        SkillCastStarted started = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.SkillCastStarted)
            .Select(message => SkillCastStarted.TryRead(message.Payload, out SkillCastStarted? read) ? read! : null!)
            .Single();
        SkillResolved resolved = ResolvedTo(server, player);
        Assert.That((started.Caster, started.Skill, started.CastMs), Is.EqualTo((crawler.Id, GraveBite, 0u)),
            "a cast of 0 ms, which the client shows with no cast bar");
        int lost = Spare - server.PlayerOf(player).CurrentHealth;
        Assert.That(lost, Is.Positive);
        Assert.That((resolved.Outcome, resolved.Amount), Is.EqualTo((SkillOutcome.Hit, (uint)lost)));
    }
}
}
