using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The quality levels delivered art relies on (the art brief, §4; Content Pipeline §6): the browser and phones use
///     "Mobile", which skins with two bone weights, the most any delivered mesh carries.
/// </summary>
[TestFixture]
public sealed class QualitySettingsTests
{
    private const string Mobile = "Mobile";

    private static SerializedObject Settings()
    {
        return new SerializedObject(QualitySettings.GetQualitySettings());
    }

    private static int LevelNamed(string name)
    {
        return Array.IndexOf(QualitySettings.names, name);
    }

    [TestCase("WebGL")]
    [TestCase("Android")]
    [TestCase("iPhone")]
    public void Platform_OfTheBrowserOrAPhone_DefaultsToMobile(string platform)
    {
        SerializedProperty defaults = Settings().FindProperty("m_PerPlatformDefaultQuality");
        var byPlatform = new Dictionary<string, int>();
        for (int index = 0; index < defaults.arraySize; index++)
        {
            SerializedProperty entry = defaults.GetArrayElementAtIndex(index);
            byPlatform[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").intValue;
        }

        Assert.That(byPlatform.TryGetValue(platform, out int level), Is.True, platform);
        Assert.That(level, Is.EqualTo(LevelNamed(Mobile)), platform);
    }

    [Test]
    public void Mobile_SkinsWithTwoBoneWeights()
    {
        int level = LevelNamed(Mobile);
        Assert.That(level, Is.Not.Negative, "a Mobile level");
        SerializedProperty skinWeights = Settings()
            .FindProperty("m_QualitySettings")
            .GetArrayElementAtIndex(level)
            .FindPropertyRelative("skinWeights");

        Assert.That((SkinWeights)skinWeights.intValue, Is.EqualTo(SkinWeights.TwoBones));
    }
}
}
