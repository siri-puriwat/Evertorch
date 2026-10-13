using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Elements, races, and sizes as content (Content Pipeline §4, §5, §7): a monster's top-level <c>element</c>,
///     <c>race</c>, and <c>size</c>, required and written to both packages, since the target frame shows them; a damage
///     skill's <c>element</c>, in both packages too; a status's server-only <c>attackElement</c>; and a consumable that
///     starts such a status.
/// </summary>
[TestFixture]
public sealed class ElementContentTests
{
    private const string Monster = "monsters/training_slime.yml";
    private const string Strike = "skills/strike.yml";
    private const string FocusSkill = "skills/focus.yml";
    private const string FocusStatus = "status-effects/focus.yml";
    private const string Potion = "items/minor_health.yml";
    private const string Converts = "    hp: 30\n    status: { status: status.focus, durationMs: 1200000 }\n";
    private const string SetsFire = "displayName: Focus\nserver:\n  attackElement: fire\n";

    private static ContentPackages Build(ContentWorkspace workspace)
    {
        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.ToString()), Is.Empty);
        return result.Packages!;
    }

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static JsonElement Definition(ContentPackage package, string path, string id)
    {
        PackageFile file = package.DataFiles.Single(candidate => candidate.Path == path);
        using (var document = JsonDocument.Parse(file.Content))
        {
            return document.RootElement.GetProperty("definitions")
                .EnumerateArray()
                .Single(definition => definition.GetProperty("id").GetString() == id)
                .Clone();
        }
    }

    private static JsonElement Slime(ContentPackage package)
    {
        PackageFile file = package.DataFiles.Single(candidate => candidate.Path == "monsters.json");
        using (var document = JsonDocument.Parse(file.Content))
        {
            return document.RootElement.GetProperty("definitions")[0].Clone();
        }
    }

    [TestCase("element: water\n", "element: lightning\n", "element", "must be one of: neutral, water, earth, fire")]
    [TestCase("element: water\n", "element: Water\n", "element", "must be one of")]
    [TestCase("element: water\n", "", "element", "required field is missing")]
    [TestCase("race: plant\n", "race: elemental\n", "race", "must be one of: formless, undead, beast, plant")]
    [TestCase("race: plant\n", "", "race", "required field is missing")]
    [TestCase("size: medium\n", "size: huge\n", "size", "must be one of: small, medium, large")]
    [TestCase("size: medium\n", "", "size", "required field is missing")]
    public void Run_WhenAFactIsBroken_ReportsTheField(
        string oldText,
        string newText,
        string expectedFieldPath,
        string expectedMessagePart)
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Monster, oldText, newText);

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(result.Packages, Is.Null);
        ContentDiagnostic diagnostic = result.Diagnostics.Single();
        Assert.That((diagnostic.File, diagnostic.FieldPath), Is.EqualTo((Monster, expectedFieldPath)));
        Assert.That(diagnostic.Message, Does.Contain(expectedMessagePart));
    }

    [TestCase(Strike, "element: weapon\n", "", "element", "is required for a skill with a damageType")]
    [TestCase(Strike, "element: weapon\n", "element: dark\n", "element", "must be one of: weapon, neutral, water")]
    [TestCase(FocusSkill, "targetType: self\n", "targetType: self\nelement: wind\n", "element",
        "is only for a skill with a damageType")]
    [TestCase(FocusStatus, "displayName: Focus\n", "displayName: Focus\nserver:\n  attackElement: weapon\n",
        "server.attackElement", "must be one of: neutral, water")]
    [TestCase(FocusStatus, "displayName: Focus\n", "displayName: Focus\nserver:\n  element: fire\n",
        "server.element", "unknown field")]
    [TestCase(Potion, "    hp: 30\n", "    hp: 30\n    status: { status: status.focus, durationMs: 0 }\n",
        "server.effect.status.durationMs", "must be between 1 and 86400000")]
    [TestCase(Potion, "    hp: 30\n", "    hp: 30\n    status: { status: status.focus, durationMs: 86400001 }\n",
        "server.effect.status.durationMs", "must be between 1 and 86400000")]
    [TestCase(Potion, "    hp: 30\n", "    hp: 30\n    status: { status: status.focus }\n",
        "server.effect.status.durationMs", "required field is missing")]
    [TestCase(Potion, "    hp: 30\n", "    hp: 30\n    status: { status: status.ember, durationMs: 1000 }\n",
        "server.effect.status.status", "references unknown status effect 'status.ember'")]
    [TestCase(Potion, "    hp: 30\n", Converts, "server.effect.status.status",
        "names 'status.focus', which sets no attack element; an item starts only such a status")]
    [TestCase(Potion, "    hp: 30\n", "    status: { status: status.focus, durationMs: 1000 }\n",
        "server.effect.status.status", "which sets no attack element")]
    public void Run_WhenAnElementOrAConvertedStatusIsBroken_ReportsTheField(
        string file,
        string oldText,
        string newText,
        string expectedFieldPath,
        string expectedMessagePart)
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(file, oldText, newText);

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(result.Packages, Is.Null);
        Assert.That(
            result.Diagnostics.Count(diagnostic =>
                diagnostic.File == file
                && diagnostic.FieldPath == expectedFieldPath
                && diagnostic.Message.Contains(expectedMessagePart, StringComparison.Ordinal)),
            Is.EqualTo(1),
            Describe(result));
    }

    [TestCase("fire", Element.Fire)]
    [TestCase("water", Element.Water)]
    [TestCase("earth", Element.Earth)]
    [TestCase("wind", Element.Wind)]
    public void RepositoryContent_EachConverter_StartsItsElementsStatusForTwentyMinutes(string name, Element element)
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty);

        AuthoredItem converter = result.Content.Items
            .Single(item => item.Definition.Id.Value == $"item.consumable.{name}_converter");
        ItemDefinition definition = converter.Definition;
        StatusEffectDefinition status = result.Content.StatusEffects
            .Single(authored => authored.Definition.Id.Value == $"status.{name}_weapon")
            .Definition;

        Assert.That(
            (definition.Type, definition.StackLimit, definition.Weight, definition.SellPrice, converter.Icon,
                converter.Model),
            Is.EqualTo((ItemType.Consumable, 50, 2, 0, "item_minor_mana", "pickup_minor_mana")),
            "the mana potion's look until its own art arrives");
        Assert.That(
            (definition.Effect!.Health, definition.Effect.Spirit, definition.Effect.Status?.Value,
                definition.Effect.StatusDurationMs),
            Is.EqualTo((0, 0, status.Id.Value, 1_200_000)));
        Assert.That(
            (definition.Market!.BasePrice, definition.Market.Floor, definition.Market.Ceiling),
            Is.EqualTo((60, 15, 600)));
        Assert.That(status.AttackElement, Is.EqualTo(element));
        Assert.That(status.DisplayName, Is.EqualTo($"{element} Weapon"));
    }

    [TestCase("skill.basic_attack", SkillElement.Weapon)]
    [TestCase("skill.strike", SkillElement.Weapon)]
    [TestCase("skill.heavy_blow", SkillElement.Weapon)]
    [TestCase("skill.arcane_bolt", SkillElement.Ghost)]
    [TestCase("skill.spark_bolt", SkillElement.Wind)]
    [TestCase("skill.quake_slam", SkillElement.Earth)]
    [TestCase("skill.grave_bite", SkillElement.Shadow)]
    public void RepositoryContent_EachDamageSkill_HasItsElement(string skill, SkillElement element)
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty);

        Assert.That(
            result.Content.Skills.Single(authored => authored.Definition.Id.Value == skill).Definition.Element,
            Is.EqualTo(element));
        Assert.That(
            result.Content.Skills
                .Where(authored => authored.Definition.DamageType == null)
                .Select(authored => authored.Definition.Element),
            Is.All.Null,
            "a skill without damage has no element");
    }

    [TestCase("monster.training_slime", Element.Water, Race.Plant, BodySize.Medium)]
    [TestCase("monster.forest_crawler", Element.Earth, Race.Insect, BodySize.Small)]
    [TestCase("monster.spark_wisp", Element.Wind, Race.Formless, BodySize.Small)]
    [TestCase("monster.grotto_crawler", Element.Earth, Race.Insect, BodySize.Medium)]
    [TestCase("monster.gloom_wisp", Element.Shadow, Race.Demon, BodySize.Small)]
    [TestCase("monster.slime_monarch", Element.Water, Race.Plant, BodySize.Large)]
    [TestCase("monster.hollow_wisp", Element.Ghost, Race.Demon, BodySize.Small)]
    [TestCase("monster.barrow_crawler", Element.Undead, Race.Undead, BodySize.Medium)]
    public void RepositoryContent_EachMonster_HasItsFacts(string monster, Element element, Race race, BodySize size)
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty);

        MonsterDefinition definition = result.Content.Monsters
            .Single(authored => authored.Definition.Id.Value == monster)
            .Definition;

        Assert.That((definition.Element, definition.Race, definition.Size), Is.EqualTo((element, race, size)));
    }

    [Test]
    public void RepositoryContent_HoldsNineStatusEffects_TheFourConvertersAloneSettingAnElement()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty);

        Assert.That(result.Content.StatusEffects, Has.Count.EqualTo(9));
        Assert.That(
            result.Content.StatusEffects
                .Where(status => status.Definition.AttackElement.HasValue)
                .Select(status => status.Definition.Id.Value)
                .OrderBy(id => id, StringComparer.Ordinal),
            Is.EqualTo(
                new[] { "status.earth_weapon", "status.fire_weapon", "status.water_weapon", "status.wind_weapon" }));
    }

    // Content Pipeline §4: a job's basic attack strikes with the weapon's element, so it names `weapon`.
    [Test]
    public void Run_ForABasicAttackOfAnotherElement_ReportsTheJob()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace("skills/basic_attack.yml", "element: weapon\n", "element: fire\n");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(result.Packages, Is.Null);
        Assert.That(
            result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath, diagnostic.Message)),
            Is.EqualTo(
                new[] { ("jobs/adventurer.yml", "server.basicAttack", "names a skill whose element is not weapon") }),
            Describe(result));
    }

    [Test]
    public void Run_ForAConsumableStartingAStatusThatSetsAnElement_WritesBothForTheServerOnly()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(FocusStatus, "displayName: Focus\n", SetsFire);
        workspace.Replace(Potion, "    hp: 30\n", Converts);

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        ItemEffect effect = result.Content.Items
            .Single(item => item.Definition.Id.Value == "item.consumable.minor_health")
            .Definition.Effect!;
        Assert.That(
            (effect.Health, effect.Spirit, effect.Status?.Value, effect.StatusDurationMs),
            Is.EqualTo((30, 0, "status.focus", 1200000)));
        Assert.That(
            result.Content.StatusEffects
                .Single(status => status.Definition.Id.Value == "status.focus")
                .Definition.AttackElement,
            Is.EqualTo(Element.Fire));

        ContentPackages packages = result.Packages!;
        JsonElement serverEffect = Definition(packages.Server, "items.json", "item.consumable.minor_health")
            .GetProperty("effect");
        Assert.That(
            (serverEffect.GetProperty("status").GetString(), serverEffect.GetProperty("durationMs").GetInt32()),
            Is.EqualTo(("status.focus", 1200000)));
        Assert.That(
            Definition(packages.Server, "status-effects.json", "status.focus").GetProperty("attackElement").GetString(),
            Is.EqualTo("fire"));
        Assert.That(
            Definition(packages.Client, "status-effects.json", "status.focus").TryGetProperty("attackElement", out _),
            Is.False);
        Assert.That(
            Definition(packages.Client, "items.json", "item.consumable.minor_health").TryGetProperty("effect", out _),
            Is.False);
    }

    [Test]
    public void Run_ForTheFixture_WritesADamageSkillsElementToBothPackages_AndNoneForAnotherSkill()
    {
        using var workspace = new ContentWorkspace();

        ContentPackages packages = Build(workspace);

        foreach (ContentPackage package in new[] { packages.Server, packages.Client })
        {
            Assert.That(
                new[] { "skill.basic_attack", "skill.strike", "skill.focus" }
                    .Select(id =>
                        Definition(package, "skills.json", id).TryGetProperty("element", out JsonElement value)
                            ? value.GetString()
                            : "-"),
                Is.EqualTo(new[] { "weapon", "weapon", "-" }));
        }
    }

    [Test]
    public void Run_ForTheFixture_WritesTheFactsToBothPackages()
    {
        using var workspace = new ContentWorkspace();

        ContentPackages packages = Build(workspace);

        foreach (JsonElement slime in new[] { Slime(packages.Server), Slime(packages.Client) })
        {
            Assert.That(
                (slime.GetProperty("element").GetString(), slime.GetProperty("race").GetString(),
                    slime.GetProperty("size").GetString()),
                Is.EqualTo(("water", "plant", "medium")));
        }
    }
}
}
