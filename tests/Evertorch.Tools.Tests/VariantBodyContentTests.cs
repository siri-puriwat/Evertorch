using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     A monster's variant body as content (Content Pipeline §4, §5; Prototype Content §2): <c>client.scale</c> and
///     <c>client.tint</c> reach the client package only when authored, and <c>boss</c> reaches both packages.
/// </summary>
[TestFixture]
public sealed class VariantBodyContentTests
{
    private const string Monster = "monsters/training_slime.yml";
    private const string Level = "level: 1\n";

    private static ContentPackages Build(ContentWorkspace workspace)
    {
        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.ToString()), Is.Empty);
        return result.Packages!;
    }

    private static JsonElement Slime(ContentPackage package)
    {
        PackageFile file = package.DataFiles.Single(candidate => candidate.Path == "monsters.json");
        using (var document = JsonDocument.Parse(file.Content))
        {
            return document.RootElement.GetProperty("definitions")[0].Clone();
        }
    }

    [TestCase("scale: 1.5", "scale: 0.2", "client.scale", "between 0.25 and 4")]
    [TestCase("scale: 1.5", "scale: 4.5", "client.scale", "between 0.25 and 4")]
    [TestCase("scale: 1.5", "scale: large", "client.scale", "finite number")]
    [TestCase("tint: \"#4A7C59\"", "tint: \"#4A7C5\"", "client.tint", "colour written \"#RRGGBB\"")]
    [TestCase("tint: \"#4A7C59\"", "tint: \"green\"", "client.tint", "colour written \"#RRGGBB\"")]
    [TestCase("tint: \"#4A7C59\"", "tint: \"#4A7C5G\"", "client.tint", "colour written \"#RRGGBB\"")]
    [TestCase("tint: \"#4A7C59\"", "tint: #4A7C59", "client.tint", "in quotes")]
    [TestCase(Level, Level + "boss: yes\n", "boss", "must be true or false")]
    public void Run_WhenTheBodyIsBroken_ReportsTheField(
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

    [Test]
    public void Run_ForABoss_WritesTheFlagToBothPackages()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Monster, Level, Level + "boss: true\n");

        ContentPackages packages = Build(workspace);

        Assert.That(Slime(packages.Server).GetProperty("boss").GetBoolean(), Is.True);
        Assert.That(Slime(packages.Client).GetProperty("boss").GetBoolean(), Is.True);
    }

    [Test]
    public void Run_ForTheFixture_WritesItsScaleAndTintToTheClientOnly_AndNoBoss()
    {
        using var workspace = new ContentWorkspace();

        ContentPackages packages = Build(workspace);

        JsonElement client = Slime(packages.Client);
        JsonElement server = Slime(packages.Server);
        Assert.That(client.GetProperty("scale").GetDouble(), Is.EqualTo(1.5d));
        Assert.That(client.GetProperty("tint").GetString(), Is.EqualTo("#4A7C59"));
        Assert.That(client.TryGetProperty("boss", out JsonElement _), Is.False, "written only for a boss");
        Assert.That(server.TryGetProperty("scale", out JsonElement _), Is.False);
        Assert.That(server.TryGetProperty("tint", out JsonElement _), Is.False);
        Assert.That(server.GetProperty("boss").GetBoolean(), Is.False);
    }

    [Test]
    public void Run_WithALowerCaseTint_WritesItInCapitals()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Monster, "tint: \"#4A7C59\"", "tint: \"#4a7c5f\"");

        ContentPackages packages = Build(workspace);

        Assert.That(Slime(packages.Client).GetProperty("tint").GetString(), Is.EqualTo("#4A7C5F"));
    }

    [Test]
    public void Run_WithoutAScaleOrATint_WritesNeitherToTheClient()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Monster, "  scale: 1.5\n", string.Empty);
        workspace.Replace(Monster, "  tint: \"#4A7C59\"\n", string.Empty);

        ContentPackages packages = Build(workspace);

        JsonElement client = Slime(packages.Client);
        Assert.That(client.TryGetProperty("scale", out JsonElement _), Is.False);
        Assert.That(client.TryGetProperty("tint", out JsonElement _), Is.False);
    }
}
}
