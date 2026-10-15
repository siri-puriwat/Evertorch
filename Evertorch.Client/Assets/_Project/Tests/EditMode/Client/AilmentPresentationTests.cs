using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     What the client makes of the ailments (Prototype Content §2; Gameplay Systems §9.1): the state flags of every
///     body, its own included, a party member's ailment bits, the words they are drawn with, the element of the moment,
///     the sheet's resistances, and poison ticks and words drawn without a flinch.
/// </summary>
[TestFixture]
public sealed class AilmentPresentationTests
{
    private static readonly EntityId Crawler = new(300);
    private static readonly SkillDefinitionId Bolt = new("skill.arcane_bolt");
    private static readonly SkillDefinitionId Lance = new("skill.frost_lance");

    private static ClientWorld CreateWorld()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(2, 8));
        world.OnSpawn(
            new EntitySpawn(
                Crawler,
                EntityKind.Monster,
                "monster.grotto_crawler",
                ClientTestGrids.Center(4, 8),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000));
        return world;
    }

    private static EntityState State(EntityId entity, WorldPosition position, EntityStateFlags flags)
    {
        return new EntityState(entity, position, new WorldDirection(0f, 1f), 0f, 0f, 0f, flags);
    }

    private static ClientMonster Monster(Element element)
    {
        return new ClientMonster(
            new MonsterDefinitionId("monster.grotto_crawler"),
            "Grotto Crawler",
            "monster_grotto_crawler",
            "icon",
            level: 14,
            element: element,
            race: Race.Insect,
            size: BodySize.Small);
    }

    [TestCase((byte)0x01, EntityStateFlags.Poisoned)]
    [TestCase((byte)0x02, EntityStateFlags.Stunned)]
    [TestCase((byte)0x04, EntityStateFlags.Frozen)]
    [TestCase((byte)0x08, EntityStateFlags.Asleep)]
    [TestCase((byte)0x10, EntityStateFlags.Silenced)]
    [TestCase((byte)0x20, EntityStateFlags.Blinded)]
    [TestCase((byte)0x3F, Ailments.All)]
    public void FromParty_ReadsAMembersBits_AsTheStateFlags(byte bits, EntityStateFlags flags)
    {
        Assert.That(Ailments.FromParty(bits), Is.EqualTo(flags));
    }

    private static string Matchups(params (string Name, int Percent)[] matchups)
    {
        return string.Join(
            " · ",
            matchups.Where(matchup => matchup.Percent != 100).Select(matchup => $"{matchup.Name} {matchup.Percent}%"));
    }

    [Test]
    public void ClientParty_KeepsAMembersAilments_FromItsStatus_ThroughANewRoster()
    {
        var party = new ClientParty();
        var roster = new PartyRoster(
            0,
            new[]
            {
                new PartyRosterEntry("Anna", new JobDefinitionId("job.adventurer"), 3, new MapDefinitionId("map.a")),
                new PartyRosterEntry("Bobby", new JobDefinitionId("job.adventurer"), 3, new MapDefinitionId("map.a"))
            });
        party.Apply(roster);
        party.Apply(new PartyMemberStatus("Bobby", 400, 900, 0x03));
        EntityStateFlags shown = party.Members[1].Ailments;
        party.Apply(roster);

        Assert.That(shown, Is.EqualTo(EntityStateFlags.Poisoned | EntityStateFlags.Stunned));
        Assert.That(party.Members[1].Ailments, Is.EqualTo(shown), "a roster keeps the status");
        Assert.That(party.Members[0].Ailments, Is.EqualTo(EntityStateFlags.None));
    }

    [Test]
    public void Facts_OfAFrozenPoisonedMonster_NameWaterAndItsAilments()
    {
        ClientMonster monster = Monster(Element.Earth);

        Assert.That(TargetFrame.Facts(monster), Is.EqualTo("Earth · Insect · Small"));
        Assert.That(
            TargetFrame.Facts(monster, EntityStateFlags.Frozen | EntityStateFlags.Poisoned | EntityStateFlags.Moving),
            Is.EqualTo("Water · Insect · Small · Poisoned · Frozen"));
        Assert.That(TargetFrame.ElementOf(monster, EntityStateFlags.Poisoned), Is.EqualTo(Element.Earth));
    }

    // The Arcanist's hint against a Water monster, and against an Earth one while it is frozen, Water of the moment:
    // Arcane Bolt deals 100 % to both, so only Frost Lance's matchup and the swing's show (Gameplay Systems §3).
    [Test]
    public void Hint_ReckonsWithTheElementOfTheMoment_FrostLanceAmongTheSkills()
    {
        var content = new ClientContent(
            "test",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster>(),
            new Dictionary<ItemDefinitionId, ClientItem>(),
            new Dictionary<SkillDefinitionId, ClientSkill>
            {
                [Bolt] = new(Bolt, "Arcane Bolt", SkillTargetType.Enemy, "skill_arcane_bolt",
                    element: SkillElement.Ghost),
                [Lance] = new(Lance, "Frost Lance", SkillTargetType.Enemy, "skill_frost_lance",
                    element: SkillElement.Water)
            },
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
        SkillListEntry[] skills =
        {
            new(Bolt, 8f, 10, 0, 500, 0, 5, 5, SkillListEntry.NoPrerequisite, 0),
            new(Lance, 8f, 12, 0, 800, 0, 5, 5, SkillListEntry.NoPrerequisite, 0)
        };
        ClientMonster earth = Monster(Element.Earth);
        int water = ElementTable.Percent(Element.Water, Element.Water);
        int fireOnWater = ElementTable.Percent(Element.Fire, Element.Water);
        int fireOnEarth = ElementTable.Percent(Element.Fire, Element.Earth);
        int waterOnEarth = ElementTable.Percent(Element.Water, Element.Earth);

        string thawed = TargetFrame.Hint(earth, Element.Fire, skills, content);
        string frozen = TargetFrame.Hint(earth, Element.Fire, skills, content,
            TargetFrame.ElementOf(earth, EntityStateFlags.Frozen));

        Assert.That(
            thawed,
            Is.EqualTo(Matchups(("Swing", fireOnEarth), ("Frost Lance", waterOnEarth))));
        Assert.That(frozen, Is.EqualTo(Matchups(("Swing", fireOnWater), ("Frost Lance", water))));
        Assert.That(frozen, Does.Contain("Frost Lance"), "Water on Water is not 100 %");
    }

    [Test]
    public void Names_FollowTheAilmentsOrder_AndShortNamesAreThePartyRowsWords()
    {
        EntityStateFlags all = Ailments.All | EntityStateFlags.Moving | EntityStateFlags.Dead;

        Assert.That(
            Ailments.Names(all),
            Is.EqualTo("Poisoned · Stunned · Frozen · Asleep · Silenced · Blinded"));
        Assert.That(Ailments.ShortNames(EntityStateFlags.Stunned | EntityStateFlags.Poisoned), Is.EqualTo("Psn Stn"));
        Assert.That(Ailments.Names(EntityStateFlags.Moving | EntityStateFlags.Dead), Is.Empty);
        Assert.That(Ailments.Of(all).Count(), Is.EqualTo(6));
        Assert.That(Ailments.Name(EntityStateFlags.Frozen), Is.EqualTo("Frozen"));
    }

    [Test]
    public void Resistances_NameEachAilmentsPercent_InOrder()
    {
        CharacterSheetStat[] stats = Enumerable.Repeat(new CharacterSheetStat(5, 2), 6).ToArray();
        var sheet = new CharacterSheet(1, new JobDefinitionId("job.vanguard"), 0, 50, 0, 0, stats, 10, 5, 2, 3, 182,
            105, 11, 153, poisonResistance: 30, stunResistance: 30, freezeResistance: 25, sleepResistance: 12,
            silenceResistance: 40, blindResistance: 40);

        Assert.That(
            BuildMessages.Resistances(sheet),
            Is.EqualTo("Resist: poison 30 · stun 30 · freeze 25 · sleep 12 · silence 40 ·"
                + " blind 40"));
    }

    // The player's first state is its baseline: an ailment it entered with, after a map change or a reconnect, did
    // not just start (review of Milestone 19).
    [Test]
    public void Snapshot_KeepsThePlayersOwnFlags_AndTellsOfEachChangeOfAnyBodysAilments()
    {
        ClientWorld world = CreateWorld();
        var changes = new List<(EntityId Entity, EntityStateFlags Before)>();
        world.AilmentsChanged += (entity, before) => changes.Add((entity, before));
        WorldPosition self = ClientTestGrids.Center(2, 8);
        WorldPosition crawler = ClientTestGrids.Center(4, 8);

        world.OnSnapshot(
            ClientWorldFixture.Snapshot(
                1,
                0,
                State(ClientWorldFixture.LocalEntity, self, EntityStateFlags.Poisoned),
                State(Crawler, crawler, EntityStateFlags.Frozen)));
        EntityStateFlags localAfterFirst = world.LocalStateFlags;
        world.OnSnapshot(
            ClientWorldFixture.Snapshot(
                2,
                0,
                State(ClientWorldFixture.LocalEntity, self, EntityStateFlags.Poisoned | EntityStateFlags.Moving),
                State(Crawler, crawler, EntityStateFlags.Frozen)));
        int afterMovingOnly = changes.Count;
        world.OnSnapshot(
            ClientWorldFixture.Snapshot(
                3,
                0,
                State(ClientWorldFixture.LocalEntity, self, EntityStateFlags.None),
                State(Crawler, crawler, EntityStateFlags.Stunned)));

        Assert.That(localAfterFirst, Is.EqualTo(EntityStateFlags.Poisoned));
        Assert.That(afterMovingOnly, Is.EqualTo(1), "the baseline and a change of movement alone tell nothing");
        Assert.That(
            changes,
            Is.EqualTo(
                new[]
                {
                    (Crawler, EntityStateFlags.None),
                    (ClientWorldFixture.LocalEntity, EntityStateFlags.Poisoned | EntityStateFlags.Moving),
                    (Crawler, EntityStateFlags.Frozen)
                }));
        Assert.That(world.LocalStateFlags, Is.EqualTo(EntityStateFlags.None));
        Assert.That(world.Remotes[Crawler].StateFlags, Is.EqualTo(EntityStateFlags.Stunned));
    }

    [Test]
    public void Timeline_ShowsAPoisonTickAndAWord_WithoutASquash()
    {
        var timeline = new CombatTimeline();
        timeline.AddHit(new HitMark(Crawler, 1.0, false, CombatResult.Poison, 12, 900));
        timeline.AddHit(new HitMark(Crawler, 1.0, false, CombatResult.None, 0, 0, word: "Frozen!"));
        timeline.AddHit(new HitMark(Crawler, 1.0, false, CombatResult.Miss, 0, 900, word: "Resisted"));
        var due = new List<HitMark>();
        timeline.CollectDueHits(1.0, 1.0, due);

        Assert.That(due.Select(hit => hit.Word), Is.EqualTo(new[] { string.Empty, "Frozen!", "Resisted" }));
        Assert.That(timeline.TryGetLastHit(Crawler, 1.0, 1.0, out _), Is.False, "nothing flinches");
        Assert.That(timeline.Squash(Crawler, 1.0, 1.0), Is.Zero);
    }
}
}
