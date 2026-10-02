using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     The boss's rules as content (Content Pipeline §4, §5, §7; Gameplay Systems §9, §10): a damage skill cast on its
///     caster may strike an area, whose radius both packages carry since the player sees it, and only a monster may cast
///     one; a spawn's respawn spread goes to the server alone.
/// </summary>
[TestFixture]
public sealed class BossContentTests
{
    private const string Monster = "monsters/training_slime.yml";
    private const string Job = "jobs/adventurer.yml";
    private const string Map = "maps/training_ground.yml";
    private const string Slam = "skills/slam.yml";

    private const string SlamSkill =
        "id: skill.slam\n"
        + "displayName: Slam\n"
        + "targetType: self\n"
        + "damageType: magical\n"
        + "area: { radius: 2.5 }\n"
        + "server:\n"
        + "  range: 0\n"
        + "  levels:\n"
        + "    - castTimeMs: { fixed: 1500 }\n"
        + "      effect:\n"
        + "        damage: { ratio: 120 }\n"
        + "client:\n"
        + "  icon: skill_slam\n";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static JsonElement Definition(ContentPackage package, string path, string id)
    {
        using var document = JsonDocument.Parse(package.DataFiles.Single(file => file.Path == path).Content);
        return document.RootElement.GetProperty("definitions")
            .EnumerateArray()
            .Single(definition => definition.GetProperty("id").GetString() == id)
            .Clone();
    }

    // The widest spread a respawn of 86,421 ms allows, a second short of it; one more is refused
    // (ContentValidationTests).
    [Test]
    public void Run_ForASpawnSpreadToItsRespawnLessASecond_WritesTheSpreadForTheServerAlone()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Map, "respawnMs: 86421", "respawnMs: 86421\n      respawnVarianceMs: 85421");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        ContentPackages packages = result.Packages!;
        Assert.That(
            Definition(packages.Server, "maps.json", "map.training_ground")
                .GetProperty("monsterSpawns")[0]
                .GetProperty("respawnVarianceMs")
                .GetInt32(),
            Is.EqualTo(85421));
        Assert.That(
            packages.Client.DataFiles.Select(file => Encoding.UTF8.GetString(file.Content)),
            Has.None.Contains("respawnVarianceMs"));
    }

    [Test]
    public void Run_ForAnAreaAMonsterCasts_WritesItsRadiusToBothPackages()
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(Slam, SlamSkill);
        workspace.Replace(Monster, "drops:", "skills:\n  - skill: skill.slam\n    chance: 0.25\ndrops:");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        ContentPackages packages = result.Packages!;
        Assert.That(
            (Definition(packages.Server, "skills.json", "skill.slam").GetProperty("areaRadius").GetDouble(),
                Definition(packages.Client, "skills.json", "skill.slam").GetProperty("area").GetDouble()),
            Is.EqualTo((2.5, 2.5)));
        Assert.That(
            Definition(packages.Server, "skills.json", "skill.strike").GetProperty("areaRadius").GetDouble(),
            Is.Zero);
        Assert.That(
            Definition(packages.Client, "skills.json", "skill.strike").TryGetProperty("area", out JsonElement _),
            Is.False,
            "the client hears of an area only where there is one");
    }

    // An area is a monster's to cast (Gameplay Systems §9).
    [Test]
    public void Run_WhenAJobListsAnAreaSkill_ReportsTheJobsSkill()
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(Slam, SlamSkill);
        workspace.Replace(Job, "skills: [skill.strike]", "skills: [skill.strike, skill.slam]");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        ContentDiagnostic diagnostic = result.Diagnostics.Single();
        Assert.That((diagnostic.File, diagnostic.FieldPath), Is.EqualTo((Job, "server.skills[1]")));
        Assert.That(diagnostic.Message, Does.Contain("strikes an area; only a monster casts one"));
    }
}
}
