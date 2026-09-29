using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
[TestFixture]
public sealed class ContentProjectionTests
{
    private const string Item = "items/slime_gel.yml";
    private const string Monster = "monsters/training_slime.yml";

    // Distinctive server-only numbers authored in the valid fixture. None may reach the client package.
    private static readonly string[] ServerOnlySentinels =
    {
        "3917", "3918", "73219", "73220", "54321", "7613", "2917", "1553", "1027", "4.0625", "1.5625", "12347",
        "6.125", "12.375", "0.7321", "1.8125", "60413", "8123", "20417", "3119", "5.1875", "3.4375", "7.5625",
        "12.6875", "14.3125", "6.875", "86421", "6.4375", "4111", "5227", "139", "30211", "50423", "80637", "77173",
        "1.6875", "4127", "3171", "7193", "5231", "21133", "1319", "146443", "61933", "71129", "0.6875", "0.3125",
        "40111", "40223", "77191", "61949", "4129", "3173", "7197", "5233", "21139", "1321"
    };

    private static readonly string[] ServerOnlyFieldNames =
    {
        "server", "weight", "sellPrice", "stats", "hp", "physicalAttack", "physicalDefense", "hit", "flee",
        "movement", "baseSpeed", "combat", "attackRange", "attackIntervalMs", "ai", "behavior", "perceptionRadius",
        "leashRadius", "drops", "chance", "amount", "minAmount", "maxAmount", "range", "damageType",
        "startingStats", "health", "spirit", "healthBase", "healthPerLevel", "spiritBase", "spiritPerLevel",
        "unarmedAttackSpeedPenalty", "startingMap", "basicAttack", "spawnPoint", "monsterSpawns", "respawnMs",
        "serverContentVersion", "roamRadius", "idlePauseMs", "idlePauseMinMs", "idlePauseMaxMs", "scanIntervalMs",
        "experienceTable", "rewards", "baseExperience", "levels", "skills", "spCost", "spPaidAt", "castTimeMs",
        "fixedCastMs", "variableCastMs", "afterCastDelayMs", "cooldownMs", "effect", "damage", "ratio", "heal",
        "damageRatio", "healHp", "statPercent", "status", "durationMs", "portals", "destination", "magicAttack",
        "keepDistance", "skill", "equipment",
        "attack", "attackSpeedPenalty", "defense", "bonus", "sp", "shop", "price", "npcs", "npc", "giver", "objective",
        "kill", "monster", "count", "currency", "jobExperienceTable", "jobExperience", "guild", "reset", "maxLevel",
        "requires", "baseJob", "weapons", "weaponType", "jobChange"
    };

    // The client package's allow-list (Content Pipeline §5), by file: a name not reviewed here fails.
    private static readonly Dictionary<string, string[]> ReviewedClientFieldNames = new()
    {
        ["items.json"] = new[]
            { "schemaVersion", "definitions", "id", "displayName", "type", "stackLimit", "icon", "model" },
        ["jobs.json"] = new[] { "schemaVersion", "definitions", "id", "displayName", "prefab" },
        ["manifest.json"] = new[] { "schemaVersion", "clientContentVersion", "files", "path", "sha256" },
        ["maps.json"] = new[]
        {
            "schemaVersion", "definitions", "id", "displayName", "scene", "navigation", "cellSize", "originX",
            "originZ", "agentRadius", "maxStepHeight", "columns", "rows", "legend", "symbol", "surface", "axis",
            "heightAtMin", "heightAtMax", "cellRows"
        },
        ["monsters.json"] = new[]
        {
            "schemaVersion", "definitions", "id", "displayName", "level", "prefab", "icon", "projectile"
        },
        ["npcs.json"] = new[] { "schemaVersion", "definitions", "id", "displayName", "prefab" },
        ["quests.json"] = new[] { "schemaVersion", "definitions", "id", "displayName" },
        ["skills.json"] = new[]
        {
            "schemaVersion", "definitions", "id", "displayName", "targetType", "icon", "description", "projectile"
        },
        ["status-effects.json"] = new[] { "schemaVersion", "definitions", "id", "displayName", "icon" }
    };

    // Authoring sections that the projection flattens into their fields, so no package carries these names.
    private static readonly string[] FlattenedAuthoringSections =
    {
        "server", "stats", "movement", "combat", "ai", "amount", "health", "spirit", "idlePauseMs", "rewards",
        "castTimeMs", "damage", "ratio", "heal", "objective", "kill", "guild"
    };

    private static ContentPackages BuildValid(ContentWorkspace workspace)
    {
        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.ToString()), Is.Empty);
        return result.Packages!;
    }

    private static void AssertManifestHashes(JsonElement manifest, ContentPackage package)
    {
        var listed = manifest
            .GetProperty("files")
            .EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("path").GetString()!,
                entry => entry.GetProperty("sha256").GetString()!);

        Assert.That(listed.Keys, Is.EquivalentTo(package.DataFiles.Select(file => file.Path)));
        foreach (PackageFile file in package.DataFiles)
        {
            Assert.That(listed[file.Path], Is.EqualTo(ContentPackageBuilder.ComputeHash(file.Content)), file.Path);
        }
    }

    private static void AssertSameBytes(ContentPackage left, ContentPackage right)
    {
        Assert.That(left.Version, Is.EqualTo(right.Version));
        Assert.That(left.Manifest.Content, Is.EqualTo(right.Manifest.Content));
        Assert.That(left.DataFiles.Select(file => file.Path), Is.EqualTo(right.DataFiles.Select(file => file.Path)));
        for (int index = 0; index < left.DataFiles.Count; index++)
        {
            Assert.That(left.DataFiles[index].Content, Is.EqualTo(right.DataFiles[index].Content));
        }
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

    private static JsonElement FirstDefinition(ContentPackage package, string path)
    {
        PackageFile file = package.DataFiles.Single(candidate => candidate.Path == path);
        using (var document = JsonDocument.Parse(file.Content))
        {
            return document.RootElement.GetProperty("definitions")[0].Clone();
        }
    }

    private static string AllText(ContentPackage package)
    {
        return string.Concat(
            package.DataFiles.Append(package.Manifest).Select(file => Encoding.UTF8.GetString(file.Content)));
    }

    // The manifest's versions and SHA-256 digests are hexadecimal, and a digest can hold a sentinel's digits by
    // chance; it did once this fixture gained its NPC.
    private static string WithoutDigests(string text)
    {
        return Regex.Replace(text, "[0-9a-f]{16,}", string.Empty);
    }

    private static void CollectPropertyNames(JsonElement element, HashSet<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                names.Add(property.Name);
                CollectPropertyNames(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                CollectPropertyNames(child, names);
            }
        }
    }

    private static HashSet<string> PropertyNamesOf(string file)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        using (var document = JsonDocument.Parse(File.ReadAllBytes(file)))
        {
            CollectPropertyNames(document.RootElement, names);
        }

        return names;
    }

    // Builds the real content with the tool's own command and returns the client files of the package and its copy.
    private static string[] BuildRepositoryClientFiles(ContentWorkspace workspace)
    {
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            string content = Path.Combine(ContentValidationTests.RepositoryRoot(), "content");
            string[] args =
            {
                "content", "build", "--content", content, "--out", workspace.OutputDirectory, "--client-out",
                workspace.ClientDirectory
            };

            int exitCode = Program.Run(args, output, error);

            Assert.That(exitCode, Is.EqualTo(0), error.ToString());
            Assert.That(error.ToString(), Is.Empty);
        }

        string[] clientFiles = Directory.GetFiles(Path.Combine(workspace.OutputDirectory, "client"), "*.json")
            .Concat(Directory.GetFiles(workspace.ClientDirectory, "*.json"))
            .ToArray();
        Assert.That(clientFiles, Has.Length.EqualTo(18), "nine files in the package and nine in its copy");
        return clientFiles;
    }

    [Test]
    public void Build_ForIdenticalInput_ProducesIdenticalBytes()
    {
        using (var first = new ContentWorkspace())
        using (var second = new ContentWorkspace())
        {
            ContentPackages left = BuildValid(first);
            ContentPackages right = BuildValid(second);

            AssertSameBytes(left.Server, right.Server);
            AssertSameBytes(left.Client, right.Client);
        }
    }

    // The first jobs give the client package only presentation (Milestone 10 verification): each of the six skills
    // its name, target type, icon, and description, `ally` for Mend alone, and a projectile for Arcane Bolt alone.
    [Test]
    public void Build_ForRepositoryContent_GivesTheFirstJobsSkillsOnlyTheirPresentation()
    {
        string[] firstJobSkills =
        {
            "skill.heavy_blow", "skill.war_cry", "skill.iron_guard", "skill.arcane_bolt", "skill.clarity",
            "skill.mend"
        };
        using (var workspace = new ContentWorkspace())
        {
            string skills = BuildRepositoryClientFiles(workspace)
                .First(file => Path.GetFileName(file) == "skills.json");
            using (var document = JsonDocument.Parse(File.ReadAllBytes(skills)))
            {
                JsonElement[] definitions = document.RootElement.GetProperty("definitions")
                    .EnumerateArray()
                    .Where(definition => firstJobSkills.Contains(definition.GetProperty("id").GetString()))
                    .ToArray();

                Assert.That(definitions, Has.Length.EqualTo(6));
                foreach (JsonElement definition in definitions)
                {
                    string id = definition.GetProperty("id").GetString()!;
                    Assert.That(
                        definition.EnumerateObject().Select(property => property.Name),
                        Is.SubsetOf(new[] { "id", "displayName", "targetType", "icon", "description", "projectile" }),
                        id);
                    Assert.That(definition.GetProperty("description").GetString(), Is.Not.Empty, id);
                    Assert.That(definition.GetProperty("icon").GetString(), Is.EqualTo(id.Replace('.', '_')), id);
                }

                Assert.That(
                    definitions.Select(definition => (definition.GetProperty("id").GetString(),
                        definition.GetProperty("targetType").GetString(),
                        definition.TryGetProperty("projectile", out JsonElement projectile)
                            ? projectile.GetString()
                            : "-")),
                    Is.EquivalentTo(
                        new[]
                        {
                            ("skill.heavy_blow", "enemy", "-"), ("skill.war_cry", "self", "-"),
                            ("skill.iron_guard", "self", "-"), ("skill.arcane_bolt", "enemy", "projectile_arcane_bolt"),
                            ("skill.clarity", "self", "-"), ("skill.mend", "ally", "-")
                        }));
            }
        }
    }

    [Test]
    public void Build_ForRepositoryContent_KeepsServerOnlyFieldNamesOutOfEveryClientFile()
    {
        using (var workspace = new ContentWorkspace())
        {
            foreach (string file in BuildRepositoryClientFiles(workspace))
            {
                Assert.That(PropertyNamesOf(file).Intersect(ServerOnlyFieldNames), Is.Empty, file);
            }

            var serverNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(Path.Combine(workspace.OutputDirectory, "server"), "*.json"))
            {
                serverNames.UnionWith(PropertyNamesOf(file));
            }

            // A name the real content no longer authors would make the check above prove nothing.
            Assert.That(serverNames, Is.SupersetOf(ServerOnlyFieldNames.Except(FlattenedAuthoringSections)));
        }
    }

    [Test]
    public void Build_ForRepositoryContent_WritesOnlyTheReviewedFieldNamesIntoEachClientFile()
    {
        using (var workspace = new ContentWorkspace())
        {
            foreach (string file in BuildRepositoryClientFiles(workspace))
            {
                Assert.That(
                    PropertyNamesOf(file).OrderBy(name => name, StringComparer.Ordinal),
                    Is.EqualTo(ReviewedClientFieldNames[Path.GetFileName(file)]
                        .OrderBy(name => name, StringComparer.Ordinal)),
                    file);
            }
        }
    }

    [Test]
    public void Build_ForValidFixture_KeepsServerOnlyFieldNamesOutOfClientPackage()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackage client = BuildValid(workspace).Client;

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PackageFile file in client.DataFiles.Append(client.Manifest))
            {
                using (var document = JsonDocument.Parse(file.Content))
                {
                    CollectPropertyNames(document.RootElement, propertyNames);
                }
            }

            Assert.That(propertyNames.Intersect(ServerOnlyFieldNames), Is.Empty);
        }
    }

    [Test]
    public void Build_ForValidFixture_KeepsServerOnlyValuesOutOfClientPackage()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackages packages = BuildValid(workspace);
            string clientText = WithoutDigests(AllText(packages.Client));
            string serverText = WithoutDigests(AllText(packages.Server));

            foreach (string sentinel in ServerOnlySentinels)
            {
                // A sentinel missing from the server package would prove nothing about the client.
                Assert.That(serverText, Does.Contain(sentinel), $"sentinel is not authored: {sentinel}");
                Assert.That(clientText, Does.Not.Contain(sentinel), $"server-only value leaked: {sentinel}");
            }
        }
    }

    [Test]
    public void Build_ForValidFixture_KeepsSourcePathsOutOfBothPackages()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackages packages = BuildValid(workspace);

            foreach (string text in new[] { AllText(packages.Client), AllText(packages.Server) })
            {
                Assert.That(text, Does.Not.Contain(".yml"));
                Assert.That(text, Does.Not.Contain(workspace.ContentRoot));
                Assert.That(text, Does.Not.Contain("\r"));
            }
        }
    }

    [Test]
    public void Build_ForValidFixture_MatchesGoldenClientPackage()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackage client = BuildValid(workspace).Client;

            string goldenDirectory = Path.Combine(ContentWorkspace.FixturesDirectory, "golden-client");
            string[] goldenFiles = Directory.GetFiles(goldenDirectory).Select(Path.GetFileName).ToArray()!;
            Assert.That(client.DataFiles.Select(file => file.Path), Is.EquivalentTo(goldenFiles));
            foreach (PackageFile file in client.DataFiles)
            {
                string expected = File.ReadAllText(Path.Combine(goldenDirectory, file.Path)).Replace("\r\n", "\n");
                Assert.That(Encoding.UTF8.GetString(file.Content), Is.EqualTo(expected), file.Path);
            }
        }
    }

    [Test]
    public void Build_ForValidFixture_WritesExperienceForTheServerOnly()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackages packages = BuildValid(workspace);

            JsonElement table = FirstDefinition(packages.Server, "experience.json");
            Assert.That(table.GetProperty("id").GetString(), Is.EqualTo("experience.adventurer"));
            Assert.That(
                table.GetProperty("levels").EnumerateArray().Select(level => level.GetInt32()),
                Is.EqualTo(new[] { 30211, 50423, 80637 }));
            Assert.That(
                FirstDefinition(packages.Server, "jobs.json").GetProperty("experienceTable").GetString(),
                Is.EqualTo("experience.adventurer"));
            Assert.That(
                FirstDefinition(packages.Server, "jobs.json").GetProperty("jobExperienceTable").GetString(),
                Is.EqualTo("experience.adventurer_job"));
            Assert.That(
                FirstDefinition(packages.Server, "monsters.json").GetProperty("baseExperience").GetInt32(),
                Is.EqualTo(77173));
            Assert.That(
                FirstDefinition(packages.Server, "monsters.json").GetProperty("jobExperience").GetInt32(),
                Is.EqualTo(77191));
            Assert.That(
                FirstDefinition(packages.Server, "quests.json").GetProperty("jobExperience").GetInt32(),
                Is.EqualTo(61949));
            Assert.That(FirstDefinition(packages.Server, "npcs.json").GetProperty("reset").GetBoolean(), Is.False);
            Assert.That(packages.Client.DataFiles.Select(file => file.Path), Does.Not.Contain("experience.json"));
        }
    }

    [Test]
    public void Build_ForValidFixture_WritesManifestsThatDescribeTheirFiles()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackages packages = BuildValid(workspace);

            using (var server = JsonDocument.Parse(packages.Server.Manifest.Content))
            using (var client = JsonDocument.Parse(packages.Client.Manifest.Content))
            {
                Assert.That(server.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(
                    server.RootElement.GetProperty("serverContentVersion").GetString(),
                    Is.EqualTo(packages.Server.Version));
                Assert.That(
                    server.RootElement.GetProperty("clientContentVersion").GetString(),
                    Is.EqualTo(packages.Client.Version));
                Assert.That(
                    client.RootElement.GetProperty("clientContentVersion").GetString(),
                    Is.EqualTo(packages.Client.Version));
                Assert.That(client.RootElement.TryGetProperty("serverContentVersion", out _), Is.False);

                AssertManifestHashes(server.RootElement, packages.Server);
                AssertManifestHashes(client.RootElement, packages.Client);
            }
        }
    }

    [Test]
    public void Build_ForValidFixture_WritesPortalsForTheServerOnly()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackages packages = BuildValid(workspace);

            JsonElement portal = FirstDefinition(packages.Server, "maps.json").GetProperty("portals").EnumerateArray()
                .Single();
            JsonElement destination = portal.GetProperty("destination");

            Assert.That(portal.GetProperty("center").GetProperty("x").GetDouble(), Is.EqualTo(6.5));
            Assert.That(portal.GetProperty("radius").GetDouble(), Is.EqualTo(1.1875));
            Assert.That(destination.GetProperty("map").GetString(), Is.EqualTo("map.training_ground"));
            Assert.That(destination.GetProperty("position").GetProperty("z").GetDouble(), Is.EqualTo(3.5));
            Assert.That(destination.GetProperty("facing").GetProperty("z").GetDouble(), Is.EqualTo(-1.0));
            Assert.That(FirstDefinition(packages.Client, "maps.json").TryGetProperty("portals", out _), Is.False);
        }
    }

    [Test]
    public void Build_ForValidFixture_WritesStatusEffectsForBoth_ButTheirNumbersForTheServerOnly()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackages packages = BuildValid(workspace);

            JsonElement server = FirstDefinition(packages.Server, "status-effects.json");
            JsonElement client = FirstDefinition(packages.Client, "status-effects.json");
            JsonElement effect = Definition(packages.Server, "skills.json", "skill.focus").GetProperty("levels")[0]
                .GetProperty("effect");
            JsonElement percent = effect.GetProperty("statPercent");

            Assert.That(server.GetProperty("id").GetString(), Is.EqualTo("status.focus"));
            Assert.That(server.TryGetProperty("statPercent", out _), Is.False, "the strength is the skill level's");
            Assert.That(
                new[] { "str", "agi", "vit", "int", "dex", "luk" }.Select(stat => percent.GetProperty(stat).GetInt32()),
                Is.EqualTo(new[] { 0, 137, 0, 0, 211, 7 }));
            Assert.That(effect.GetProperty("status").GetString(), Is.EqualTo("status.focus"));
            Assert.That(effect.GetProperty("durationMs").GetInt32(), Is.EqualTo(43117));
            Assert.That(
                client.EnumerateObject().Select(property => property.Name),
                Is.EqualTo(new[] { "id", "displayName", "icon" }));
        }
    }

    [Test]
    public void Build_ForValidFixture_WritesTheSkillNumbersAndTheJobsSkillsForTheServer()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPackages packages = BuildValid(workspace);

            JsonElement strike = Definition(packages.Server, "skills.json", "skill.strike");
            JsonElement first = strike.GetProperty("levels")[0];
            JsonElement second = strike.GetProperty("levels")[1];
            Assert.That(strike.GetProperty("damageType").GetString(), Is.EqualTo("physical"));
            Assert.That(strike.GetProperty("spPaidAt").GetString(), Is.EqualTo("castStart"));
            Assert.That(strike.GetProperty("maxLevel").GetInt32(), Is.EqualTo(2));
            Assert.That(strike.TryGetProperty("requires", out _), Is.False, "no prerequisite writes none");
            Assert.That(first.GetProperty("spCost").GetInt32(), Is.EqualTo(4127));
            Assert.That(first.GetProperty("fixedCastMs").GetInt32(), Is.EqualTo(3171));
            Assert.That(first.GetProperty("variableCastMs").GetInt32(), Is.EqualTo(7193));
            Assert.That(first.GetProperty("afterCastDelayMs").GetInt32(), Is.EqualTo(5231));
            Assert.That(first.GetProperty("cooldownMs").GetInt32(), Is.EqualTo(21133));
            Assert.That(first.GetProperty("effect").GetProperty("damageRatio").GetInt32(), Is.EqualTo(1319));
            Assert.That(second.GetProperty("effect").GetProperty("damageRatio").GetInt32(), Is.EqualTo(1321));
            JsonElement basicAttack = Definition(packages.Server, "skills.json", "skill.basic_attack");
            Assert.That(
                (basicAttack.GetProperty("maxLevel").GetInt32(), basicAttack.GetProperty("levels").GetArrayLength()),
                Is.EqualTo((0, 0)),
                "a skill without an effect has no levels");
            Assert.That(
                FirstDefinition(packages.Server, "jobs.json").GetProperty("skills").EnumerateArray()
                    .Select(skill => skill.GetString()),
                Is.EqualTo(new[] { "skill.strike" }));
        }
    }

    [Test]
    public void Build_WhenClientPresentationChanges_AdvancesClientVersion()
    {
        using (var original = new ContentWorkspace())
        using (var restyled = new ContentWorkspace())
        {
            restyled.Replace(Item, "icon: item_slime_gel", "icon: item_slime_gel_v2");

            ContentPackages before = BuildValid(original);
            ContentPackages after = BuildValid(restyled);

            Assert.That(after.Client.Version, Is.Not.EqualTo(before.Client.Version));
            Assert.That(after.Server.Version, Is.EqualTo(before.Server.Version));
        }
    }

    [Test]
    public void Build_WhenFileNamesSortDifferently_OrdersDefinitionsById()
    {
        using (var original = new ContentWorkspace())
        using (var renamed = new ContentWorkspace())
        {
            renamed.Move(Item, "items/aaa_first_on_disk.yml");
            renamed.Move("items/minor_health.yml", "items/zzz_last_on_disk.yml");

            AssertSameBytes(BuildValid(original).Server, BuildValid(renamed).Server);
            AssertSameBytes(BuildValid(original).Client, BuildValid(renamed).Client);
        }
    }

    [Test]
    public void Build_WhenOnlyServerDataChanges_KeepsClientFilesAndVersion()
    {
        using (var original = new ContentWorkspace())
        using (var rebalanced = new ContentWorkspace())
        {
            rebalanced.Replace(Monster, "chance: 0.7321", "chance: 0.25");
            rebalanced.Replace(Item, "sellPrice: 73219", "sellPrice: 5");
            rebalanced.Replace(Monster, "baseExperience: 77173", "baseExperience: 3");
            rebalanced.Replace("experience/adventurer.yml", "30211", "30");

            ContentPackages before = BuildValid(original);
            ContentPackages after = BuildValid(rebalanced);

            Assert.That(after.Server.Version, Is.Not.EqualTo(before.Server.Version));
            Assert.That(after.Client.Version, Is.EqualTo(before.Client.Version));
            AssertSameBytes(before.Client, after.Client);
        }
    }

    [Test]
    public void Build_WithAProjectile_WritesItForTheClientMonster_AndNothingWithout()
    {
        using (var without = new ContentWorkspace())
        using (var with = new ContentWorkspace())
        {
            with.Replace(
                "monsters/training_slime.yml",
                "icon: monster_training_slime_icon",
                "icon: monster_training_slime_icon\n  projectile: projectile_spark");

            JsonElement plain = FirstDefinition(BuildValid(without).Client, "monsters.json");
            ContentPackages packages = BuildValid(with);
            JsonElement flying = FirstDefinition(packages.Client, "monsters.json");

            Assert.That(plain.TryGetProperty("projectile", out _), Is.False);
            Assert.That(flying.GetProperty("projectile").GetString(), Is.EqualTo("projectile_spark"));
            Assert.That(FirstDefinition(packages.Server, "monsters.json").TryGetProperty("projectile", out _),
                Is.False);
        }
    }
}
}
