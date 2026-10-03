using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Evertorch.Game;
using Evertorch.Tools;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The loader reads NPCs, their shops, quests, and where maps place NPCs, and refuses what the tools refuse of them
///     but a place to stand that can be walked to, which stays the tools' check as a portal's reachability does
///     (Content Pipeline §7, §10).
/// </summary>
[TestFixture]
public sealed class ServerContentNpcTests
{
    private const string Maps = "maps.json";
    private const string Npcs = "npcs.json";
    private const string Quests = "quests.json";
    private const string Ground = "map.training_ground";
    private const string Quartermaster = "npc.quartermaster";

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

    private static Dictionary<string, byte[]> WithPlacements(string placements)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.SetValue(files, Maps, Ground, "npcs", placements);
        return files;
    }

    private static string Placement(string npc, float x, float y, float z)
    {
        return FormattableString.Invariant(
            $"{{\"npc\":\"{npc}\",\"position\":{{\"x\":{x},\"y\":{y},\"z\":{z}}},\"facing\":{{\"x\":0,\"z\":-1}}}}");
    }

    [TestCase(Npcs, Quartermaster, "shop[0].price", "1", "npc.quartermaster: sells 'item.material.slime_gel' for 1, "
        + "below its sell price of 2")]
    [TestCase(Npcs, Quartermaster, "shop[0].price", "0", "definitions[0].shop[0].price: must be at least 1")]
    [TestCase(Npcs, Quartermaster, "shop[0].item", "\"item.material.goo\"",
        "npc.quartermaster: sells unknown item 'item.material.goo'")]
    [TestCase(Npcs, Quartermaster, "shop[0].item", "\"monster.goo\"",
        "definitions[0].shop[0].item: 'monster.goo' is not a valid ID of this kind")]
    [TestCase(Quests, "quest.slime_hunt", "count", "0", "definitions[0].count: must be at least 1")]
    [TestCase(Quests, "quest.slime_hunt", "giver", "\"npc.nobody\"",
        "quest.slime_hunt: is given by unknown NPC 'npc.nobody'")]
    [TestCase(Quests, "quest.slime_hunt", "monster", "\"monster.ghost\"",
        "quest.slime_hunt: asks for unknown monster 'monster.ghost'")]
    [TestCase(Maps, Ground, "npcs[0].position.x", "-2.5",
        "map.training_ground: NPC 'npc.quartermaster' does not stand on an NPC marker cell")]
    [TestCase(Maps, Ground, "npcs[0].position.y", "0.5",
        "map.training_ground: NPC 'npc.quartermaster' does not stand at its marker's height")]
    [TestCase(Maps, Ground, "npcs[0].facing.z", "0", "definitions[0].npcs[0].facing: must not be the zero direction")]
    public void Load_WithOneDefectInAnNpcOrQuest_ReportsExactlyThatDefect(
        string file,
        string id,
        string path,
        string value,
        string expected)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.SetValue(files, file, id, path, value);

        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { $"{file}: {expected}" }));
    }

    // The fixture NPC gives one quest (154 bytes) beside the header (14), so its shop may trade 11 items of 74 bytes:
    // 982 bytes, where 12 items take 1,056 of the 1,020 one message carries.
    [TestCase(11, false)]
    [TestCase(12, true)]
    public void Load_ForAnNpcsServices_TakesWhatFitsOneMessage(int items, bool isRefused)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        IEnumerable<string> extra = Enumerable.Range(1, items - 1).Select(index =>
            $"{{\"id\":\"item.material.extra_{index:D2}\",\"displayName\":\"Extra\",\"type\":\"material\","
            + "\"stackLimit\":1,\"weight\":1,\"sellPrice\":1}");
        string gel = PackageFixture.ReadText(files, "items.json");
        int start = gel.IndexOf('[', StringComparison.Ordinal) + 1;
        PackageFixture.Replace(files, "items.json", gel.Substring(0, start),
            $"{gel.Substring(0, start)}{string.Join(",", extra)},");

        Assert.That(
            ProblemsOf(files),
            isRefused
                ? Is.EqualTo(
                    new[]
                    {
                        "npcs.json: npc.quartermaster: its services take 1056 bytes, more than the 1020 one message "
                        + "carries"
                    })
                : Is.Empty);
    }

    [Test]
    public void Load_ForFixtureContent_ReadsTheNpcItsShopItsPlacementAndTheQuestTheToolsParsed()
    {
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.FixtureContentDirectory);
        NpcDefinition authoredNpc = pipeline.Content.Npcs.Single().Definition;
        QuestDefinition authoredQuest = pipeline.Content.Quests.Single().Definition;
        NpcPlacement authoredPlacement = pipeline.Content.Maps.Single().Definition.Npcs.Single();

        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildFixturePackage());

        NpcDefinition npc = content.Npcs[authoredNpc.Id];
        Assert.That(npc.DisplayName, Is.EqualTo(authoredNpc.DisplayName));
        Assert.That(
            npc.Shop.Select(entry => (entry.Item, entry.Price)),
            Is.EqualTo(authoredNpc.Shop.Select(entry => (entry.Item, entry.Price))));
        QuestDefinition quest = content.Quests[authoredQuest.Id];
        Assert.That(
            (quest.DisplayName, quest.Giver, quest.Monster, quest.Count, quest.BaseExperience, quest.Currency),
            Is.EqualTo(
                (authoredQuest.DisplayName, authoredQuest.Giver, authoredQuest.Monster, authoredQuest.Count,
                    authoredQuest.BaseExperience, authoredQuest.Currency)));
        NpcPlacement placement = content.Maps[new MapDefinitionId(Ground)].Npcs.Single();
        Assert.That(
            (placement.Npc, placement.Position, placement.Facing),
            Is.EqualTo((authoredPlacement.Npc, authoredPlacement.Position, authoredPlacement.Facing)));
    }

    [Test]
    public void Load_ForRepositoryContent_ReadsTheFourNpcsAndTheQuests()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(
            content.Npcs.Keys.Select(id => id.Value),
            Is.EquivalentTo(new[] { "npc.gate_warden", "npc.guildmaster", "npc.quartermaster", "npc.storekeeper" }));
        Assert.That(content.Npcs[new NpcDefinitionId(Quartermaster)].Shop, Has.Count.EqualTo(5));
        Assert.That(content.Npcs[new NpcDefinitionId("npc.gate_warden")].HasShop, Is.False);
        Assert.That(
            content.Npcs.Values.Where(npc => npc.OffersReset).Select(npc => npc.Id.Value),
            Is.EqualTo(new[] { "npc.guildmaster" }));
        Assert.That(
            content.Npcs.Values.Where(npc => npc.KeepsStorage)
                .Select(npc => (npc.Id.Value, npc.DepositFee, npc.HasShop)),
            Is.EqualTo(new[] { ("npc.storekeeper", (int?)20, false) }));
        Assert.That(
            content.Quests.Keys.Select(id => id.Value),
            Is.EquivalentTo(new[] { "quest.crawler_hunt", "quest.grotto_hunt", "quest.slime_monarch" }));
        Assert.That(
            content.Maps[new MapDefinitionId(Ground)].Npcs.Select(npc => npc.Npc.Value),
            Is.EqualTo(new[] { Quartermaster, "npc.gate_warden", "npc.guildmaster", "npc.storekeeper" }));
        Assert.That(content.Maps[new MapDefinitionId("map.training_field")].Npcs, Is.Empty);
    }

    [Test]
    public void Load_WhenAMapPlacesAnUnknownNpc_ReportsItAndTheQuestWhoseGiverNoMapPlaces()
    {
        Dictionary<string, byte[]> files = WithPlacements($"[{Placement("npc.nobody", -3.5f, 0f, 4.5f)}]");

        Assert.That(
            ProblemsOf(files),
            Is.EquivalentTo(
                new[]
                {
                    "maps.json: map.training_ground: places unknown NPC 'npc.nobody'",
                    "quests.json: quest.slime_hunt: is given by NPC 'npc.quartermaster', which no map places"
                }));
    }

    [Test]
    public void Load_WhenAQuestRewardsNothing_RefusesIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.SetValue(files, Quests, "quest.slime_hunt", "baseExperience", "0");
        PackageFixture.SetValue(files, Quests, "quest.slime_hunt", "currency", "0");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(
                new[]
                {
                    "quests.json: definitions[0].baseExperience: a quest must reward base experience, job "
                    + "experience, or coins"
                }));
    }

    [Test]
    public void Load_WhenAShopListsAnItemTwice_RefusesIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.SetValue(
            files,
            Npcs,
            Quartermaster,
            "shop",
            "[{\"item\":\"item.material.slime_gel\",\"price\":4},{\"item\":\"item.material.slime_gel\",\"price\":5}]");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[]
                { "npcs.json: definitions[0].shop[1].item: 'item.material.slime_gel' appears more than once" }));
    }

    [Test]
    public void Load_WhenAnNpcIsPlacedTwice_RefusesTheSecondPlacement()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Maps, Ground, "npcs[1].npc", "\"npc.quartermaster\"");

        Assert.That(
            ProblemsOf(files),
            Is.EquivalentTo(
                new[]
                {
                    "maps.json: map.training_ground: places NPC 'npc.quartermaster', which 'map.training_ground' "
                    + "already places",
                    "quests.json: quest.crawler_hunt: is given by NPC 'npc.gate_warden', which no map places",
                    "quests.json: quest.grotto_hunt: is given by NPC 'npc.gate_warden', which no map places",
                    "quests.json: quest.slime_monarch: is given by NPC 'npc.gate_warden', which no map places"
                }));
    }

    [Test]
    public void Load_WhenTwoNpcsStandOnOneMarker_RefusesTheMap()
    {
        string placement = Placement(Quartermaster, -3.5f, 0f, 4.5f);
        Dictionary<string, byte[]> files = WithPlacements($"[{placement},{placement}]");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(
                new[]
                {
                    "maps.json: map.training_ground: NPC 'npc.quartermaster' stands on a marker cell another NPC "
                    + "stands on"
                }));
    }

    // A character keeps every quest it takes, and one quest log carries them all.
    [Test]
    public void Load_WithMoreQuestsThanAQuestLogCarries_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        JsonArray definitions = JsonNode.Parse(files[Quests])!["definitions"]!.AsArray();
        JsonNode quest = definitions[0]!;
        definitions.Clear();
        for (int index = 1; index <= 15; index++)
        {
            JsonNode copy = quest.DeepClone();
            copy["id"] = $"quest.hunt_{index:D2}";
            definitions.Add(copy);
        }

        files[Quests] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);

        Assert.That(ProblemsOf(files), Does.Contain("quests.json: lists more than the 14 quests a quest log carries"));
    }
}
}
