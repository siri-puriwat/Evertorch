using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The loader reads a monster's element, race, and size, and refuses what the tools refuse of them (Content
///     Pipeline §7; Gameplay Systems §10). The repository's package carries every monster's; an edit re-signs it.
/// </summary>
[TestFixture]
public sealed class ServerContentElementTests
{
    private const string Monsters = "monsters.json";
    private const string Slime = "monster.training_slime";

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

    [TestCase("element", "\"lightning\"", "unknown value 'lightning'")]
    [TestCase("element", "\"Water\"", "unknown value 'Water'")]
    [TestCase("element", "3", "must be a string")]
    [TestCase("race", "\"elemental\"", "unknown value 'elemental'")]
    [TestCase("size", "\"huge\"", "unknown value 'huge'")]
    public void Load_ForAFactOutOfItsList_RefusesIt(string path, string json, string message)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string reported = PackageFixture.SetValue(files, Monsters, Slime, path, json);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Some.StartsWith($"{Monsters}: {reported}").And.Contains(message),
            string.Join("\n", problems));
    }

    [Test]
    public void Load_ForTheRepositoryContent_GivesEachMonsterItsFacts()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(
            content.Monsters.Values
                .OrderBy(monster => monster.Id.Value, StringComparer.Ordinal)
                .Select(monster => $"{monster.Id.Value} {monster.Element} {monster.Race} {monster.Size}"),
            Is.EqualTo(
                new[]
                {
                    "monster.forest_crawler Earth Insect Small", "monster.gloom_wisp Shadow Demon Small",
                    "monster.grotto_crawler Earth Insect Medium", "monster.slime_monarch Water Plant Large",
                    "monster.spark_wisp Wind Formless Small", "monster.training_slime Water Plant Medium"
                }));
    }

    [Test]
    public void Load_WithoutAFact_RefusesTheMonster()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Monsters, "\"race\": \"plant\",", string.Empty);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("definitions[0].race: required property is"));
    }
}
}
