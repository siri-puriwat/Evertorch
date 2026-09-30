using System;
using System.Collections.Generic;
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
    public void ReadManifest_ForADelivery_ReadsItsAnchorsMarkersAndCalibration()
    {
        ArtManifest manifest = ArtManifest.Parse(Manifest);

        Assert.That(manifest.Delivery, Is.EqualTo("ArtDelivery-99-Test"));
        ArtManifestAsset asset = manifest.Assets[0];
        Assert.That((asset.Key, asset.Kind, asset.PickRadius), Is.EqualTo(("monster_a", "monster", 0.8f)));
        Assert.That(asset.Anchors["Anchor_Overhead"], Is.EqualTo(new Vector3(0f, 0.92f, 0f)));
        Assert.That(manifest.Clips[0].CalibrationSpeed, Is.EqualTo(4f));
        Assert.That(manifest.Clips[1].Markers["impact"], Is.EqualTo(24));
        Assert.That(manifest.Clips[1].Loop, Is.False);
    }

    [Test]
    public void ReadManifest_WhenItsRateIsNotThirty_Throws()
    {
        Assert.Throws<FormatException>(() => ArtManifest.Parse(Manifest.Replace(@"""fps"": 30", @"""fps"": 24")));
    }

    [Test]
    public void ReadHashes_ForTheDeliveryFormat_KeysEachHashByItsPath()
    {
        IReadOnlyDictionary<string, string> hashes = ArtDeliveryImporter.ReadHashes(
            new string('A', 64) + "  Humanoid/a.fbx\r\n" + new string('b', 64) + "  manifest.json\r\n");

        Assert.That(hashes["Humanoid/a.fbx"], Is.EqualTo(new string('a', 64)));
        Assert.That(hashes["manifest.json"], Is.EqualTo(new string('b', 64)));
    }
}
}
