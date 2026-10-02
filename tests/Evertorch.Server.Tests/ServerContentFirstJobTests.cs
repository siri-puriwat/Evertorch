using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Evertorch.Game;
using Evertorch.Rules;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The loader's side of the first jobs (Content Pipeline §4, §7): it resolves a first job from its base job in a
///     second pass, as the tools do, and refuses what the tools refuse, so a hand-edited package cannot carry a first
///     job the tools would not write.
/// </summary>
[TestFixture]
public sealed class ServerContentFirstJobTests
{
    private const string Items = "items.json";
    private const string Jobs = "jobs.json";
    private const string Npcs = "npcs.json";
    private const string Skills = "skills.json";
    private const string Vanguard = "job.vanguard";

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

    // Removes one property of the definition id and re-signs the manifest; returns the definition's path.
    private static string Remove(Dictionary<string, byte[]> files, string file, string id, string property)
    {
        JsonArray definitions = JsonNode.Parse(PackageFixture.ReadText(files, file))!["definitions"]!.AsArray();
        int index = definitions.ToList().FindIndex(definition => (string?)definition!["id"] == id);
        definitions[index]!.AsObject().Remove(property);
        files[file] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);
        return $"definitions[{index}]";
    }

    [TestCase("startingMap", "\"map.training_ground\"")]
    [TestCase("baseSpeed", "5")]
    [TestCase("unarmedAttackSpeedPenalty", "44")]
    public void Load_WhenAFirstJobCarriesAnInheritedField_RefusesIt(string field, string json)
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string path = PackageFixture.SetValue(files, Jobs, Vanguard, field, json);

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { $"{Jobs}: {path}: is taken from the base job, so a first job omits it" }));
    }

    [TestCase("\"job.missing\"", "its base job 'job.missing' is unknown")]
    [TestCase("\"job.arcanist\"", "its base job 'job.arcanist' has a base job of its own")]
    public void Load_WhenAFirstJobsBaseJobIsUnknownOrAFirstJob_RefusesIt(string baseJob, string problem)
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        PackageFixture.SetValue(files, Jobs, Vanguard, "baseJob", baseJob);

        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { $"{Jobs}: {Vanguard}: {problem}" }));
    }

    [TestCase("[]", "must list at least one weapon type")]
    [TestCase("[\"axe\"]", "'axe' is not a weapon type or appears more than once")]
    [TestCase("[\"sword\",\"sword\"]", "'sword' is not a weapon type or appears more than once")]
    public void Load_WhenAJobsWeaponsAreBroken_RefusesThem(string weapons, string message)
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string path = PackageFixture.SetValue(files, Jobs, Vanguard, "weapons", weapons);

        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { $"{Jobs}: {path}: {message}" }));
    }

    [Test]
    public void Load_ForRepositoryContent_ResolvesTheFirstJobsAsTheToolsDo()
    {
        ServerContent content = ServerContentLoader.Load(RepositoryPackage());

        JobDefinition adventurer = content.Jobs[new JobDefinitionId("job.adventurer")];
        foreach (string id in new[] { Vanguard, "job.arcanist" })
        {
            JobDefinition job = content.Jobs[new JobDefinitionId(id)];
            Assert.That(job.BaseJob, Is.EqualTo(adventurer.Id), id);
            Assert.That(
                (job.StartingStats, job.StartingMap, job.ExperienceTable, job.BasicAttack, job.BaseSpeed,
                    job.UnarmedAttackSpeedPenalty),
                Is.EqualTo(
                    (adventurer.StartingStats, adventurer.StartingMap, adventurer.ExperienceTable,
                        adventurer.BasicAttack, adventurer.BaseSpeed, adventurer.UnarmedAttackSpeedPenalty)),
                id);
            Assert.That(job.Tree.Take(3), Is.EqualTo(adventurer.Tree), $"{id}: the Adventurer's tree first");
            Assert.That(job.Tree.Skip(3), Is.EqualTo(job.Skills), $"{id}: then its own");
            Assert.That(job.CarriedSkillPoints, Is.EqualTo(9), id);
        }

        Assert.That(
            content.Jobs[new JobDefinitionId(Vanguard)].Weapons,
            Is.EqualTo(new[] { WeaponType.Sword }));
        Assert.That(
            content.Jobs[new JobDefinitionId("job.arcanist")].Weapons,
            Is.EqualTo(new[] { WeaponType.Staff }));
        Assert.That(adventurer.Weapons, Is.EqualTo(new[] { WeaponType.Sword, WeaponType.Staff }));
        Assert.That(
            content.Items[new ItemDefinitionId("item.weapon.training_staff")].Equipment!.WeaponType,
            Is.EqualTo(WeaponType.Staff));
        Assert.That(content.Npcs[new NpcDefinitionId("npc.guildmaster")].OffersJobChange, Is.True);
        Assert.That(
            content.Skills[new SkillDefinitionId("skill.mend")].TargetType,
            Is.EqualTo(SkillTargetType.Ally));
    }

    [Test]
    public void Load_WhenAFirstJobRepeatsItsBaseTree_RefusesIt()
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        PackageFixture.SetValue(files, Jobs, Vanguard, "skills[0]", "\"skill.strike\"");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { $"{Jobs}: {Vanguard}: skill 'skill.strike' repeats one of its base job's tree" }));
    }

    // Three base skills and nine own make twelve, one more than a skill list carries.
    [Test]
    public void Load_WhenAFirstJobsWholeTreePassesElevenSkills_RefusesIt()
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string[] own =
        {
            "skill.heavy_blow", "skill.war_cry", "skill.iron_guard", "skill.arcane_bolt", "skill.clarity",
            "skill.mend", "skill.spark_bolt", "skill.basic_attack", "skill.extra"
        };
        PackageFixture.SetValue(files, Jobs, Vanguard, "skills",
            $"[{string.Join(",", own.Select(id => $"\"{id}\""))}]");

        Assert.That(
            ProblemsOf(files),
            Does.Contain($"{Jobs}: {Vanguard}: its whole tree holds 12 skills, more than the 11 a skill list carries"));
    }

    [Test]
    public void Load_WhenAJobListsNoWeapons_RefusesIt()
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string definition = Remove(files, Jobs, Vanguard, "weapons");

        Assert.That(ProblemsOf(files),
            Is.EqualTo(new[] { $"{Jobs}: {definition}.weapons: required property is missing" }));
    }

    [Test]
    public void Load_WhenAWeaponHasNoTypeOrAnArmorHasOne_RefusesIt()
    {
        Dictionary<string, byte[]> weaponless = RepositoryPackage();
        JsonArray definitions = JsonNode.Parse(PackageFixture.ReadText(weaponless, Items))!["definitions"]!.AsArray();
        int sword = definitions.ToList().FindIndex(item => (string?)item!["id"] == "item.weapon.training_sword");
        definitions[sword]!["equipment"]!.AsObject().Remove("weaponType");
        weaponless[Items] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(weaponless);
        Dictionary<string, byte[]> typedArmor = RepositoryPackage();
        string armor = PackageFixture.SetValue(typedArmor, Items, "item.armor.cloth", "equipment.weaponType",
            "\"sword\"");

        Assert.That(
            ProblemsOf(weaponless),
            Is.EqualTo(new[] { $"{Items}: definitions[{sword}].equipment.weaponType: required property is missing" }));
        Assert.That(ProblemsOf(typedArmor), Is.EqualTo(new[] { $"{Items}: {armor}: is only for a weapon" }));
    }

    [Test]
    public void Load_WhenAnAllySkillDoesNotHeal_RefusesIt()
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string path = PackageFixture.SetValue(files, Skills, "skill.first_aid", "targetType", "\"ally\"");

        Assert.That(ProblemsOf(files), Is.Empty, "a heal may target an ally");
        path = PackageFixture.SetValue(files, Skills, "skill.strike", "targetType", "\"ally\"");
        Assert.That(
            ProblemsOf(files),
            Is.EquivalentTo(
                new[]
                {
                    $"{Skills}: {path}: must be enemy for a damage effect, or self with an area",
                    $"{Skills}: {path}: may be ally only for a heal effect"
                }));
    }

    [Test]
    public void Load_WhenAnNpcDoesNotSayWhetherItChangesJobs_RefusesIt()
    {
        Dictionary<string, byte[]> files = RepositoryPackage();
        string definition = Remove(files, Npcs, "npc.guildmaster", "jobChange");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { $"{Npcs}: {definition}.jobChange: required property is missing" }));
    }

    // A new character starts in a base job; a first job is reached only at the Guildmaster (Gameplay Systems §6.1).
    [Test]
    public void World_WhenTheStartingJobIsAFirstJob_RefusesToStart()
    {
        ServerContent content = ServerContentLoader.Load(RepositoryPackage());
        IOptions<WorldOptions> options = Options.Create(new WorldOptions { StartingJob = Vanguard });

        Action start = () => _ = new WorldSimulation(
            content,
            options,
            new CharacterStats(new RenewalCharacterRules()),
            new RenewalMovementRules(),
            new SeededRandomSource(1));

        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("is a first job"));
    }
}
}
