using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Skill levels, prerequisites, and descriptions as content (Content Pipeline §4, §5, §7; Gameplay Systems §9).
/// </summary>
[TestFixture]
public sealed class SkillLevelContentTests
{
    private const string Job = "jobs/adventurer.yml";
    private const string Strike = "skills/strike.yml";
    private const string Focus = "skills/focus.yml";
    private const string JobSkills = "skills: [skill.strike]";
    private const string TreeWithFocus = "skills: [skill.strike, skill.focus]";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static ContentPipelineResult WithFocusRequiring(string requires, string? strikeRequires = null)
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Job, JobSkills, TreeWithFocus);
        workspace.Replace(Focus, "  range: 0\n", $"  range: 0\n  requires: {requires}\n");
        if (strikeRequires != null)
        {
            workspace.Replace(Strike, "  range: 1.6875\n", $"  range: 1.6875\n  requires: {strikeRequires}\n");
        }

        return ContentPipeline.Run(workspace.ContentRoot);
    }

    [TestCase("{ skill: skill.basic_attack, level: 1 }", "not another of the tree")]
    [TestCase("{ skill: skill.focus, level: 1 }", "not another of the tree")]
    [TestCase("{ skill: skill.strike, level: 3 }", "above its maximum 2")]
    [TestCase("{ skill: skill.strike, level: 6 }", "between 1 and 5")]
    public void Run_WhenAPrerequisiteIsOutsideTheTreeOrAboveItsMaximum_ReportsIt(string requires, string message)
    {
        ContentPipelineResult result = WithFocusRequiring(requires);

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Message), Has.Some.Contains(message),
            Describe(result));
    }

    [Test]
    public void Build_ForRepositoryContent_WritesTheDescriptionsToTheClientPackage()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Packages, Is.Not.Null, Describe(result));

        using var skills = JsonDocument.Parse(
            result.Packages!.Client.DataFiles.Single(file => file.Path == "skills.json").Content);
        JsonElement strike = skills.RootElement.GetProperty("definitions").EnumerateArray()
            .Single(skill => skill.GetProperty("id").GetString() == "skill.strike");

        Assert.That(strike.GetProperty("description").GetString(), Does.StartWith("A heavy blow"));
        Assert.That(strike.TryGetProperty("levels", out _), Is.False, "the levels stay on the server");
    }

    [Test]
    public void Run_ForRepositoryContent_GivesTheAdventurersTreeItsLevelsAndFocusItsPrerequisite()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        SkillDefinition strike = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.strike")
            .Definition;
        SkillDefinition firstAid = result.Content.Skills
            .Single(skill => skill.Definition.Id.Value == "skill.first_aid")
            .Definition;
        SkillDefinition focus = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.focus")
            .Definition;
        Assert.That(
            (strike.MaxLevel, firstAid.MaxLevel, focus.MaxLevel),
            Is.EqualTo((5, 1, 3)),
            "9 points spend the whole tree");
        Assert.That(
            strike.Levels.Select(level => level.Effect.DamageRatioPercent),
            Is.EqualTo(new[] { 130, 145, 160, 175, 190 }));
        Assert.That(
            (strike.ValuesAt(1).SpCost, strike.ValuesAt(1).AfterCastDelayMs, strike.ValuesAt(1).CooldownMs),
            Is.EqualTo((8, 500, 2000)),
            "Strike 1 keeps Milestone 6's values");
        Assert.That(firstAid.ValuesAt(1).Effect.HealHp, Is.EqualTo(15));
        Assert.That(
            focus.Levels.Select(level => (level.Effect.StatPercent.Agi, level.Effect.StatPercent.Dex)),
            Is.EqualTo(new[] { (40, 40), (70, 70), (100, 100) }),
            "Focus 3 is Milestone 6's +100 %");
        Assert.That(focus.Levels.Select(level => level.SpCost), Is.All.EqualTo(15));
        Assert.That(focus.Levels.Select(level => level.Effect.StatusDurationMs), Is.All.EqualTo(60_000));
        Assert.That((focus.Requires!.Skill.Value, focus.Requires.Level), Is.EqualTo(("skill.strike", 1)));
        Assert.That(
            result.Content.Skills.Where(skill => skill.Description != null).Select(skill => skill.Definition.Id.Value),
            Is.EquivalentTo(new[] { "skill.strike", "skill.first_aid", "skill.focus" }),
            "each skill of the tree is described");
    }

    [Test]
    public void Run_WhenAPrerequisiteIsAnotherSkillOfTheTreeAtOneOfItsLevels_IsValid()
    {
        ContentPipelineResult result = WithFocusRequiring("{ skill: skill.strike, level: 2 }");

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
    }

    [Test]
    public void Run_WhenPrerequisitesFormACycle_ReportsIt()
    {
        ContentPipelineResult result = WithFocusRequiring(
            "{ skill: skill.strike, level: 1 }",
            "{ skill: skill.focus, level: 1 }");

        Assert.That(
            result.Diagnostics.Select(diagnostic => diagnostic.Message),
            Has.Some.Contains("in a cycle of prerequisites"),
            Describe(result));
    }
}
}
