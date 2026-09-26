using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Consumables (Content Pipeline §4; Gameplay Systems §11.2): a server-only effect that restores HP, SP, or both.
/// </summary>
[TestFixture]
public sealed class ConsumableContentTests
{
    private const string Potion = "items/minor_health.yml";

    private static JsonElement Item(ContentPackage package, string id)
    {
        PackageFile file = package.DataFiles.Single(candidate => candidate.Path == "items.json");
        using (var document = JsonDocument.Parse(file.Content))
        {
            return document.RootElement.GetProperty("definitions")
                .EnumerateArray()
                .Single(definition => definition.GetProperty("id").GetString() == id)
                .Clone();
        }
    }

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [TestCase("  effect:\n    hp: 30\n", "", "server.effect", "required field is missing")]
    [TestCase("    hp: 30\n", "    hp: 0\n", "server.effect", "must restore HP, SP, or both")]
    [TestCase("    hp: 30\n", "    hp: -1\n", "server.effect.hp", "between 0 and")]
    [TestCase("    hp: 30\n", "    hp: 30\n    mp: 3\n", "server.effect.mp", "unknown field")]
    public void Run_WhenAConsumableIsBroken_ReportsThatField(
        string oldText,
        string newText,
        string field,
        string message)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Potion, oldText, newText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.File == Potion && diagnostic.FieldPath == field && diagnostic.Message.Contains(message)),
                Is.True,
                Describe(result));
        }
    }

    [Test]
    public void Build_WithAConsumable_WritesItsEffectForTheServerOnly()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Potion, "    hp: 30\n", "    hp: 30\n    sp: 5\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            JsonElement effect = Item(result.Packages!.Server, "item.consumable.minor_health").GetProperty("effect");
            Assert.That(
                (effect.GetProperty("hp").GetInt32(), effect.GetProperty("sp").GetInt32()),
                Is.EqualTo((30, 5)));
            JsonElement client = Item(result.Packages.Client, "item.consumable.minor_health");
            Assert.That(client.TryGetProperty("effect", out _), Is.False);
            Assert.That(client.GetProperty("type").GetString(), Is.EqualTo("consumable"));
        }
    }

    [Test]
    public void Run_WhenAMaterialHasAnEffect_ReportsIt()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace("items/slime_gel.yml", "  sellPrice: 73219\n",
                "  sellPrice: 73219\n  effect:\n    hp: 1\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.FieldPath, diagnostic.Message)),
                Has.Some.EqualTo(("server.effect", "is only for a consumable")),
                Describe(result));
        }
    }
}
}
