using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     The first debuff as content (Content Pipeline §4, §7; Gameplay Systems §9.1): a status level's percentage runs
///     from −99 to 1,000, a skill may put its status on an enemy, a job may not list one, and the Gloom Wisp casts
///     Numbing Spark, AGI −40 % for 15 s.
/// </summary>
[TestFixture]
public sealed class DebuffContentTests
{
    private const string Focus = "skills/focus.yml";
    private const string Job = "jobs/adventurer.yml";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static ContentPipelineResult RepositoryContent()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        return result;
    }

    [TestCase("agi: -99", true)]
    [TestCase("agi: -100", false)]
    public void Run_ForAStatusLevelBelowZero_AcceptsDownToMinus99(string percent, bool isValid)
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Focus, "agi: 137", percent);

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        if (isValid)
        {
            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            using var document = JsonDocument.Parse(
                result.Packages!.Server.DataFiles.Single(file => file.Path == "skills.json").Content);
            Assert.That(
                document.RootElement.GetProperty("definitions").EnumerateArray()
                    .Single(skill => skill.GetProperty("id").GetString() == "skill.focus")
                    .GetProperty("levels")[0].GetProperty("effect").GetProperty("statPercent").GetProperty("agi")
                    .GetInt32(),
                Is.EqualTo(-99));
        }
        else
        {
            ContentDiagnostic diagnostic = result.Diagnostics.Single();
            Assert.That(diagnostic.FieldPath, Is.EqualTo("server.levels[0].effect.status.statPercent.agi"));
            Assert.That(diagnostic.Message, Does.Contain("between -99 and 1000"));
        }
    }

    [Test]
    public void Run_ForRepositoryContent_GivesTheGloomWispItsNumbingSpark()
    {
        ContentPipelineResult result = RepositoryContent();

        AuthoredMonster wisp = result.Content.Monsters
            .Single(monster => monster.Definition.Id.Value == "monster.gloom_wisp");
        SkillDefinition spark = result.Content.Skills
            .Single(skill => skill.Definition.Id.Value == "skill.numbing_spark")
            .Definition;
        SkillLevel level = spark.Levels.Single();
        Assert.That(
            (wisp.Definition.Level, wisp.Definition.Behavior, wisp.Definition.KeepDistance),
            Is.EqualTo((16, MonsterBehavior.Passive, 3.5)));
        Assert.That(
            wisp.Definition.Skills.Select(entry => (entry.Skill.Value, entry.Chance)),
            Is.EqualTo(new[] { ("skill.numbing_spark", 0.15) }));
        Assert.That((wisp.Prefab, wisp.Scale, wisp.Tint), Is.EqualTo(("monster_spark_wisp", (double?)null, "#6A4C9C")));
        Assert.That(spark.TargetType, Is.EqualTo(SkillTargetType.Enemy));
        Assert.That(
            (level.Effect.Status.Value, level.Effect.StatusDurationMs, level.Effect.StatPercent),
            Is.EqualTo(("status.numbed", 15000, new StatPercentages(0, -40, 0, 0, 0, 0))));
        Assert.That((level.FixedCastMs, level.CooldownMs), Is.EqualTo((800, 8000)));
    }

    // A status on an enemy is a monster's to cast; a job's statuses are its own (Gameplay Systems §9.1).
    [Test]
    public void Run_WhenAJobListsAStatusCastAtAnEnemy_ReportsTheJobsSkill()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Focus, "targetType: self", "targetType: enemy");
        workspace.Replace(Job, "skills: [skill.strike]", "skills: [skill.strike, skill.focus]");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        ContentDiagnostic diagnostic = result.Diagnostics.Single();
        Assert.That((diagnostic.File, diagnostic.FieldPath), Is.EqualTo((Job, "server.skills[1]")));
        Assert.That(diagnostic.Message, Does.Contain("puts a status on an enemy"));
    }
}
}
