using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Client.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ManifestJsonTests
{
    private const string Manifest = @"{
  ""delivery"": ""ArtDelivery-99-Test"",
  ""fps"": 30,
  ""assets"": [
    {
      ""key"": ""monster_a"",
      ""kind"": ""monster"",
      ""file"": ""Monsters/monster_a/monster_a.fbx"",
      ""triangles"": 100,
      ""bones"": 3,
      ""maximumWeights"": 1,
      ""materials"": [""A""],
      ""textures"": [""Monsters/monster_a/Textures/monster_a_BaseMap.png""],
      ""anchors"": { ""Anchor_Overhead"": [0, 0.92, 0], ""Anchor_Pick"": [0, 0.28, 0] },
      ""pickRadius"": 0.8
    }
  ],
  ""clips"": [
    {
      ""rig"": ""monster_a"",
      ""name"": ""move"",
      ""file"": ""Monsters/monster_a/Animations/monster_a@move.fbx"",
      ""frames"": 18,
      ""loop"": true,
      ""markers"": {},
      ""calibrationSpeedMetresPerSecond"": 4,
      ""contactFrameWindows"": [[0, 2]]
    },
    {
      ""rig"": ""monster_a"",
      ""name"": ""attack"",
      ""file"": ""Monsters/monster_a/Animations/monster_a@attack.fbx"",
      ""frames"": 36,
      ""loop"": false,
      ""markers"": { ""impact"": 24 }
    }
  ]
}";

    private const string WornManifest = @"{
  ""delivery"": ""ArtDelivery-99-Worn"",
  ""fps"": 30,
  ""assets"": [
    {
      ""key"": ""worn_test_boots"",
      ""kind"": ""worn"",
      ""category"": ""footwear"",
      ""file"": ""Worn/worn_test_boots.fbx"",
      ""triangles"": 540,
      ""vertices"": 410,
      ""materials"": [""Body""],
      ""textures"": [""Worn/Textures/worn_test_boots_BaseMap.png""],
      ""pickup"": ""pickup_test_boots"",
      ""parts"": [
        { ""node"": ""Boot_L"", ""socket"": ""Socket_Foot_L"" },
        { ""node"": ""Boot_R"", ""socket"": ""Socket_Foot_R"" }
      ]
    }
  ],
  ""clips"": [],
  ""sockets"": {
    ""character_test"": {
      ""Socket_Foot_L"": {
        ""bone"": ""LeftFoot"",
        ""position"": [0.0, 0.02, 0.03],
        ""rotation"": [0.1, 0.2, 0.3, 0.9],
        ""scale"": [1.0, 1.1, 1.2]
      }
    }
  }
}";

    [TestCase("\"key\": \"monster_a\"", "\"key\": \"../monster_a\"")]
    [TestCase("\"rig\": \"monster_a\",\n      \"name\": \"move\"", "\"rig\": \"Monster A\",\n      \"name\": \"move\"")]
    [TestCase("\"delivery\": \"ArtDelivery-99-Test\"", "\"delivery\": \"../Elsewhere\"")]
    [TestCase("\"file\": \"Monsters/monster_a/monster_a.fbx\"", "\"file\": \"Monsters/../../monster_a.fbx\"")]
    [TestCase("Textures/monster_a_BaseMap.png", "Textures\\\\monster_a_BaseMap.png")]
    public void ReadManifest_WhenANameCouldLeaveTheArtFolder_Throws(string oldText, string newText)
    {
        Assert.That(Manifest, Does.Contain(oldText));
        Assert.Throws<FormatException>(() => ArtManifest.Parse(Manifest.Replace(oldText, newText)));
    }

    [TestCase("\"category\": \"footwear\"", "\"category\": \"boots\"")]
    [TestCase("\"pickup\": \"pickup_test_boots\"", "\"pickup\": \"Pickup Boots\"")]
    [TestCase("\"vertices\": 410,", "")]
    [TestCase("\"node\": \"Boot_L\"", "\"node\": \"Boot/L\"")]
    [TestCase("[0.1, 0.2, 0.3, 0.9]", "[0.1, 0.2, 0.3]")]
    [TestCase("\"bone\": \"LeftFoot\"", "\"bone\": \"../LeftFoot\"")]
    public void ReadManifest_WhenAWornPieceOrSocketIsBroken_Throws(string oldText, string newText)
    {
        Assert.That(WornManifest, Does.Contain(oldText));
        Assert.Throws<FormatException>(() => ArtManifest.Parse(WornManifest.Replace(oldText, newText)));
    }

    [Test]
    public void Parse_ForEscapesInAString_ReadsTheCharacters()
    {
        object? value = ManifestJson.Parse(@"""a\""b\\c\/d\nA""");

        Assert.That(value, Is.EqualTo("a\"b\\c/d\nA"));
    }

    [Test]
    public void Parse_ForNestedValues_ReadsObjectsArraysNumbersAndWords()
    {
        var root = (Dictionary<string, object?>)ManifestJson.Parse(
            @"{ ""a"": [1, -2.5e1, true, false, null], ""b"": {} }")!;

        Assert.That(root["a"], Is.EqualTo(new List<object?> { 1.0, -25.0, true, false, null }));
        Assert.That(root["b"], Is.Empty);
    }

    [Test]
    public void Parse_WhenAKeyRepeats_Throws()
    {
        Assert.Throws<FormatException>(() => ManifestJson.Parse(@"{ ""a"": 1, ""a"": 2 }"));
    }

    [Test]
    public void Parse_WhenAStringIsUnterminated_Throws()
    {
        Assert.Throws<FormatException>(() => ManifestJson.Parse(@"{ ""a"": ""b }"));
    }

    [Test]
    public void Parse_WhenTextFollowsTheValue_Throws()
    {
        Assert.Throws<FormatException>(() => ManifestJson.Parse("{} {}"));
    }

    [Test]
    public void ReadHashes_ForTheDeliveryFormat_KeysEachHashByItsPath()
    {
        IReadOnlyDictionary<string, string> hashes = ArtDeliveryImporter.ReadHashes(
            new string('A', 64) + "  Humanoid/a.fbx\r\n" + new string('b', 64) + "  manifest.json\r\n");

        Assert.That(hashes["Humanoid/a.fbx"], Is.EqualTo(new string('a', 64)));
        Assert.That(hashes["manifest.json"], Is.EqualTo(new string('b', 64)));
    }

    [Test]
    public void ReadManifest_ForAClipInNoMonstersFolder_Throws()
    {
        string moved = Manifest.Replace(
            "Monsters/monster_a/Animations/monster_a@attack.fbx",
            "Monsters/monster_b/Animations/monster_b@attack.fbx");

        Assert.Throws<FormatException>(() => ArtManifest.Parse(moved));
    }

    [Test]
    public void ReadManifest_ForADelivery_ReadsItsAnchorsMarkersAndCalibration()
    {
        var manifest = ArtManifest.Parse(Manifest);

        Assert.That(manifest.Delivery, Is.EqualTo("ArtDelivery-99-Test"));
        ArtManifestAsset asset = manifest.Assets[0];
        Assert.That((asset.Key, asset.Kind, asset.PickRadius), Is.EqualTo(("monster_a", "monster", 0.8f)));
        Assert.That(asset.Anchors["Anchor_Overhead"], Is.EqualTo(new Vector3(0f, 0.92f, 0f)));
        Assert.That(manifest.Clips[0].CalibrationSpeed, Is.EqualTo(4f));
        Assert.That(manifest.Clips[1].Markers["impact"], Is.EqualTo(24));
        Assert.That(manifest.Clips[1].Loop, Is.False);
    }

    [Test]
    public void ReadManifest_ForAMonsterClipUnderAnotherRigName_GivesItToTheMonsterWhoseFolderHoldsIt()
    {
        var manifest = ArtManifest.Parse(Manifest.Replace(@"""rig"": ""monster_a""", @"""rig"": ""a"""));

        Assert.That(manifest.Clips.Select(clip => clip.Rig), Is.EqualTo(new[] { "monster_a", "monster_a" }));
    }

    [Test]
    public void ReadManifest_ForAMonsterOutsideAnyFolder_Throws()
    {
        string loose = Manifest.Replace(
            "\"file\": \"Monsters/monster_a/monster_a.fbx\"",
            "\"file\": \"monster_a.fbx\"");

        Assert.That(loose, Does.Contain("\"file\": \"monster_a.fbx\""));
        Assert.Throws<FormatException>(() => ArtManifest.Parse(loose));
    }

    [Test]
    public void ReadManifest_ForAMoveWithoutASpeedTheArtBriefGives_Throws()
    {
        string unknown = Manifest.Replace(@"""calibrationSpeedMetresPerSecond"": 4,", string.Empty);

        Assert.Throws<FormatException>(() => ArtManifest.Parse(unknown));
    }

    [Test]
    public void ReadManifest_ForAMoveWithoutItsSpeed_TakesTheArtBriefsSpeed()
    {
        string crawler = Manifest.Replace("monster_a", "monster_forest_crawler")
            .Replace(@"""calibrationSpeedMetresPerSecond"": 4,", string.Empty);

        var manifest = ArtManifest.Parse(crawler);

        Assert.That(manifest.Clips[0].CalibrationSpeed, Is.EqualTo(4.8f));
    }

    [Test]
    public void ReadManifest_ForAWornPieceWithoutParts_Throws()
    {
        int start = WornManifest.IndexOf("[\n        { \"node\"", StringComparison.Ordinal);
        int end = WornManifest.IndexOf("]\n    }\n  ],", StringComparison.Ordinal);
        string bare = WornManifest.Substring(0, start) + "[" + WornManifest.Substring(end);

        Assert.Throws<FormatException>(() => ArtManifest.Parse(bare));
    }

    [Test]
    public void ReadManifest_ForAWornPiece_ReadsItsCategoryPickupPartsAndTheSockets()
    {
        var manifest = ArtManifest.Parse(WornManifest);

        ArtManifestAsset boots = manifest.Assets.Single();
        Assert.That(
            (boots.Kind, boots.Category, boots.Triangles, boots.Vertices, boots.Pickup),
            Is.EqualTo(("worn", "footwear", 540, 410, "pickup_test_boots")));
        Assert.That(boots.Anchors, Is.Empty, "a worn piece names no anchors");
        Assert.That(
            boots.Parts.Select(part => (part.Node, part.Socket)),
            Is.EqualTo(new[] { ("Boot_L", "Socket_Foot_L"), ("Boot_R", "Socket_Foot_R") }));
        ArtManifestSocket socket = manifest.Sockets.Single();
        Assert.That((socket.Outfit, socket.Name, socket.Bone),
            Is.EqualTo(("character_test", "Socket_Foot_L", "LeftFoot")));
        Assert.That(socket.Position, Is.EqualTo(new Vector3(0f, 0.02f, 0.03f)));
        Assert.That(socket.Rotation, Is.EqualTo(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f)), "x, y, z, w");
        Assert.That(socket.Scale, Is.EqualTo(new Vector3(1f, 1.1f, 1.2f)));
        Assert.That(ArtManifest.Parse(Manifest).Sockets, Is.Empty, "a delivery without worn pieces has none");
    }

    // A monster's clips belong to the deepest monster folder that holds them, and a monster whose model lies in no
    // folder of its own claims no clip.
    [Test]
    public void ReadManifest_ForNestedMonsterFolders_GivesAClipToTheDeepest()
    {
        string nested = Manifest.Replace(
                "      \"pickRadius\": 0.8\n    }\n  ],",
                "      \"pickRadius\": 0.8\n    },\n    {\n      \"key\": \"monster_b\",\n"
                + "      \"kind\": \"monster\",\n      \"file\": \"Monsters/monster_a/monster_b/monster_b.fbx\",\n"
                + "      \"triangles\": 100,\n      \"bones\": 3,\n      \"materials\": [\"B\"],\n"
                + "      \"textures\": [],\n      \"anchors\": {},\n      \"pickRadius\": 0.5\n    }\n  ],")
            .Replace(
                "Monsters/monster_a/Animations/monster_a@attack.fbx",
                "Monsters/monster_a/monster_b/Animations/monster_b@attack.fbx");

        var manifest = ArtManifest.Parse(nested);

        Assert.That(manifest.Assets, Has.Count.EqualTo(2));
        Assert.That(manifest.Clips.Select(clip => clip.Rig), Is.EqualTo(new[] { "monster_a", "monster_b" }));
    }

    [Test]
    public void ReadManifest_WhenItsRateIsNotThirty_Throws()
    {
        Assert.Throws<FormatException>(() => ArtManifest.Parse(Manifest.Replace(@"""fps"": 30", @"""fps"": 24")));
    }

    [Test]
    public void ReadManifest_WithoutMaximumWeights_ReadsTheAsset()
    {
        var manifest = ArtManifest.Parse(Manifest.Replace(@"""maximumWeights"": 1,", string.Empty));

        Assert.That(manifest.Assets[0].Key, Is.EqualTo("monster_a"));
    }
}
}
