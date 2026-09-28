using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The loader refuses every number outside the bounds the tools check, and the two shapes the tools refuse that it
///     let through, so a hand-edited package cannot carry what the tools would not write (Content Pipeline §7; finding
///     5 of the Milestone 6 review).
/// </summary>
[TestFixture]
public sealed class ServerContentBoundsTests
{
    private const string Items = "items.json";
    private const string Monsters = "monsters.json";
    private const string Skills = "skills.json";
    private const string Jobs = "jobs.json";
    private const string Experience = "experience.json";
    private const string StatusEffects = "status-effects.json";
    private const string Maps = "maps.json";
    private const string Npcs = "npcs.json";
    private const string Quests = "quests.json";
    private const string Gel = "item.material.slime_gel";
    private const string Slime = "monster.training_slime";
    private const string Wisp = "monster.spark_wisp";
    private const string Ground = "map.training_ground";
    private const string Adventurer = "job.adventurer";
    private const string AtMostAMillion = "must be at most 1000000";
    private const string AtMostABillion = "must be at most 1000000000";
    private const string AtMostAStat = "must be at most 9999";
    private const string AtMostADay = "must be at most 86400000";
    private const string AtMostADistance = "must be at most 10000";
    private const string OnTheMap = "must be between -100000 and 100000";

    private static readonly Lazy<Dictionary<string, byte[]>> Repository =
        new(PackageFixture.BuildRepositoryPackage);

    private static Dictionary<string, byte[]> RepositoryPackage()
    {
        return new Dictionary<string, byte[]>(Repository.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ProblemsOf(IReadOnlyDictionary<string, byte[]> files)
    {
        try
        {
            ServerContentLoader.Load(files);
        }
        catch (ContentLoadException exception)
        {
            return exception.Problems;
        }

        return Array.Empty<string>();
    }

    [TestCase(Items, Gel, "stackLimit", "1000001", AtMostAMillion)]
    [TestCase(Items, Gel, "weight", "1000001", AtMostAMillion)]
    [TestCase(Items, Gel, "sellPrice", "1000000001", AtMostABillion)]
    [TestCase(Items, "item.weapon.training_sword", "equipment.attack", "10000", AtMostAStat)]
    [TestCase(Items, "item.weapon.training_sword", "equipment.attackSpeedPenalty", "10000", AtMostAStat)]
    [TestCase(Items, "item.armor.cloth", "equipment.defense", "10000", AtMostAStat)]
    [TestCase(Items, "item.weapon.training_staff", "equipment.bonus.int", "10000", AtMostAStat)]
    [TestCase(Items, "item.consumable.minor_health", "effect.hp", "10000", AtMostAStat)]
    [TestCase(Items, "item.consumable.minor_mana", "effect.sp", "10000", AtMostAStat)]
    [TestCase(Monsters, Slime, "level", "1000", "must be at most 999")]
    [TestCase(Monsters, Slime, "hp", "100000001", "must be at most 100000000")]
    [TestCase(Monsters, Slime, "physicalAttack", "10000", AtMostAStat)]
    [TestCase(Monsters, Slime, "physicalDefense", "10000", AtMostAStat)]
    [TestCase(Monsters, Slime, "hit", "10000", AtMostAStat)]
    [TestCase(Monsters, Slime, "flee", "10000", AtMostAStat)]
    [TestCase(Monsters, Wisp, "magicAttack", "10000", AtMostAStat)]
    [TestCase(Monsters, Slime, "baseSpeed", "101", "must be at most 100")]
    [TestCase(Monsters, Slime, "attackRange", "0", "must be greater than 0")]
    [TestCase(Monsters, Slime, "attackRange", "10001", AtMostADistance)]
    [TestCase(Monsters, Slime, "attackIntervalMs", "86400001", AtMostADay)]
    [TestCase(Monsters, Slime, "perceptionRadius", "10001", AtMostADistance)]
    [TestCase(Monsters, Slime, "leashRadius", "0", "must be greater than 0")]
    [TestCase(Monsters, Slime, "leashRadius", "10001", AtMostADistance)]
    [TestCase(Monsters, Slime, "roamRadius", "10001", AtMostADistance)]
    [TestCase(Monsters, Slime, "idlePauseMaxMs", "86400001", AtMostADay)]
    [TestCase(Monsters, Slime, "scanIntervalMs", "86400001", AtMostADay)]
    [TestCase(Monsters, Slime, "baseExperience", "1000000001", AtMostABillion)]
    [TestCase(Monsters, Slime, "drops[0].maxAmount", "1000001", AtMostAMillion)]
    [TestCase(Skills, "skill.strike", "range", "10001", AtMostADistance)]
    [TestCase(Skills, "skill.strike", "spCost", "100000001", "must be at most 100000000")]
    [TestCase(Skills, "skill.strike", "fixedCastMs", "86400001", AtMostADay)]
    [TestCase(Skills, "skill.strike", "variableCastMs", "86400001", AtMostADay)]
    [TestCase(Skills, "skill.strike", "afterCastDelayMs", "86400001", AtMostADay)]
    [TestCase(Skills, "skill.strike", "cooldownMs", "86400001", AtMostADay)]
    [TestCase(Skills, "skill.strike", "effect.damageRatio", "10001", "must be at most 10000")]
    [TestCase(Skills, "skill.first_aid", "effect.healHp", "100000001", "must be at most 100000000")]
    [TestCase(Skills, "skill.focus", "effect.durationMs", "86400001", AtMostADay)]
    [TestCase(Jobs, Adventurer, "startingStats.str", "100", "must be at most 99")]
    [TestCase(Jobs, Adventurer, "startingStats.luk", "100", "must be at most 99")]
    [TestCase(Jobs, Adventurer, "healthBase", "100000001", "must be at most 100000000")]
    [TestCase(Jobs, Adventurer, "healthPerLevel", "100000001", "must be at most 100000000")]
    [TestCase(Jobs, Adventurer, "spiritBase", "100000001", "must be at most 100000000")]
    [TestCase(Jobs, Adventurer, "spiritPerLevel", "100000001", "must be at most 100000000")]
    [TestCase(Jobs, Adventurer, "unarmedAttackSpeedPenalty", "201", "must be at most 200")]
    [TestCase(Jobs, Adventurer, "baseSpeed", "0", "must be greater than 0")]
    [TestCase(Jobs, Adventurer, "baseSpeed", "101", "must be at most 100")]
    [TestCase(Experience, "experience.adventurer", "levels[0]", "1000000001", AtMostABillion)]
    [TestCase(StatusEffects, "status.focus", "statPercent.agi", "1001", "must be at most 1000")]
    [TestCase(Maps, Ground, "spawnPoint.position.x", "100001", OnTheMap)]
    [TestCase(Maps, Ground, "spawnPoint.facing.z", "100001", OnTheMap)]
    [TestCase(Maps, Ground, "portals[0].center.x", "-100001", OnTheMap)]
    [TestCase(Maps, Ground, "portals[0].radius", "10001", AtMostADistance)]
    [TestCase(Maps, Ground, "portals[0].destination.position.z", "-100001", OnTheMap)]
    [TestCase(Maps, Ground, "portals[0].destination.facing.x", "100001", OnTheMap)]
    [TestCase(Maps, Ground, "monsterSpawns[0].center.z", "100001", OnTheMap)]
    [TestCase(Maps, Ground, "monsterSpawns[0].radius", "10001", AtMostADistance)]
    [TestCase(Maps, Ground, "monsterSpawns[0].count", "1001", "must be at most 1000")]
    [TestCase(Maps, Ground, "monsterSpawns[0].respawnMs", "86400001", AtMostADay)]
    [TestCase(Maps, Ground, "npcs[0].position.x", "100001", OnTheMap)]
    [TestCase(Maps, Ground, "npcs[0].facing.z", "100001", OnTheMap)]
    [TestCase(Maps, Ground, "navigation.cellSize", "101", "must be at most 100")]
    [TestCase(Maps, Ground, "navigation.originX", "-100001", OnTheMap)]
    [TestCase(Maps, Ground, "navigation.agentRadius", "11", "must be at most 10")]
    [TestCase(Maps, Ground, "navigation.maxStepHeight", "101", "must be at most 100")]
    [TestCase(Maps, Ground, "navigation.legend[0].heightAtMin", "100001", OnTheMap)]
    [TestCase(Npcs, "npc.quartermaster", "shop[0].price", "1000000001", AtMostABillion)]
    [TestCase(Quests, "quest.crawler_hunt", "count", "1001", "must be at most 1000")]
    [TestCase(Quests, "quest.crawler_hunt", "baseExperience", "1000000001", AtMostABillion)]
    [TestCase(Quests, "quest.crawler_hunt", "currency", "1000000001", AtMostABillion)]
    public void Load_WhenANumberIsOutsideTheToolsBounds_ReportsExactlyThatField(
        string file,
        string id,
        string path,
        string value,
        string message)
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string field = PackageFixture.SetValue(files, file, id, path, value);

        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { $"{file}: {field}: {message}" }));
    }

    [TestCase(998, false)]
    [TestCase(999, true)]
    public void Load_ForAnExperienceTable_TakesAtMostOneLevelBelowTheLevelLimit(int levels, bool isRefused)
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string table = $"[{string.Join(",", Enumerable.Repeat("10", levels))}]";
        string field = PackageFixture.SetValue(files, Experience, "experience.adventurer", "levels", table);

        Assert.That(
            ProblemsOf(files),
            isRefused ? Is.EqualTo(new[] { $"{Experience}: {field}: must list at most 998 levels" }) : Is.Empty);
    }

    // The ground's own legend, then entries no cell uses.
    [TestCase(52, false)]
    [TestCase(53, true)]
    public void Load_ForALegend_TakesAtMostOneLetterPerCellKind(int entries, bool isRefused)
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        JsonArray legend = JsonNode.Parse(files[Maps])!["definitions"]!.AsArray()
            .Single(map => (string?)map!["id"] == Ground)!["navigation"]!["legend"]!.AsArray();
        var used = new HashSet<string>(legend.Select(entry => (string)entry!["symbol"]!), StringComparer.Ordinal);
        string[] unused = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
            .Select(symbol => symbol.ToString())
            .Where(symbol => !used.Contains(symbol))
            .ToArray();
        IEnumerable<string> extra = unused.Take(entries - legend.Count).Select(symbol =>
            $"{{\"symbol\":\"{symbol}\",\"surface\":\"wall\",\"axis\":\"none\",\"heightAtMin\":0,\"heightAtMax\":0}}");
        string all = $"[{string.Join(",", legend.Select(entry => entry!.ToJsonString()).Concat(extra))}]";
        string field = PackageFixture.SetValue(files, Maps, Ground, "navigation.legend", all);

        Assert.That(
            ProblemsOf(files),
            isRefused ? Is.EqualTo(new[] { $"{Maps}: {field}: has more than 52 entries" }) : Is.Empty);
    }

    [Test]
    public void Load_ForRepositoryContent_IsWithinEveryBound()
    {
        Assert.That(ProblemsOf(RepositoryPackage()), Is.Empty);
    }

    [Test]
    public void Load_WhenAMonsterListsASkillTwice_RefusesIt()
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string field = PackageFixture.SetValue(
            files,
            Monsters,
            Wisp,
            "skills",
            "[{\"skill\":\"skill.spark_bolt\",\"chance\":0.5},{\"skill\":\"skill.spark_bolt\",\"chance\":0.5}]");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { $"{Monsters}: {field}[1].skill: 'skill.spark_bolt' appears more than once" }));
    }

    [Test]
    public void Load_WhenARampIsNotFloor_RefusesIt()
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string field = PackageFixture.SetValue(files, Maps, Ground, "navigation.legend[0].axis", "\"x\"");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { $"{Maps}: {field.Replace(".axis", ".surface")}: a ramp must be floor" }));
    }
}
}
