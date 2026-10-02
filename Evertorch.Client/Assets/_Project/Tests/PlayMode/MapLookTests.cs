using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using Object = UnityEngine.Object;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A map's look (Prototype Content §2): the graybox takes its colours from the scene's <see cref="MapLook" />, and
///     the Umbral Grotto's scene is dark and fogged, with a colour for every surface.
/// </summary>
public sealed class MapLookTests
{
    private const string GrottoScene = "12_UmbralGrotto";

    private static readonly int SurfaceCount = Enum.GetValues(typeof(GrayboxSurface)).Length;

    // A material keeps a colour to within float rounding.
    private static readonly ColorEqualityComparer Colours = new(1e-4f);

    private readonly List<Object> m_created = new();

    [TearDown]
    public void DestroyCreated()
    {
        foreach (Object created in m_created)
        {
            Object.DestroyImmediate(created);
        }

        m_created.Clear();
    }

    private static NavigationGrid CreateYard()
    {
        const int size = 4;
        var cells = new NavigationCell[size * size];
        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = NavigationCell.Level(index == 0 ? NavigationSurface.Wall : NavigationSurface.Floor, 0f);
        }

        return new NavigationGrid(size, size, 1f, 0f, 0f, 0.3f, 0.4f, cells);
    }

    private Color[] SurfaceColours(IReadOnlyList<Color>? palette)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m_created.Add(material);
        var map = GrayboxMap.Create(CreateYard(), material, palette);
        m_created.Add(map.gameObject);
        return map.GetComponent<MeshRenderer>().sharedMaterials.Select(surface => surface.color).ToArray();
    }

    [Test]
    public void GrayboxMap_WithAPalette_ColoursEverySurfaceFromIt()
    {
        Color[] palette = Enumerable.Range(0, SurfaceCount)
            .Select(index => new Color(index / 10f, 0.5f, 1f - index / 10f))
            .ToArray();

        Assert.That(SurfaceColours(palette), Is.EqualTo(palette).Using(Colours));
    }

    [Test]
    public void GrayboxMap_WithAShortPalette_KeepsTheDefaultColourOfTheRest()
    {
        Color[] defaults = SurfaceColours(null);
        var floor = new Color(0.1f, 0.2f, 0.3f);

        Color[] shown = SurfaceColours(new[] { floor });

        Assert.That(shown[(int)GrayboxSurface.Floor], Is.EqualTo(floor).Using(Colours));
        Assert.That(shown.Skip(1), Is.EqualTo(defaults.Skip(1)).Using(Colours));
    }

    // The scene is loaded beside the test runner's and made active, since its ambient and fog are the active scene's.
    [UnityTest]
    public IEnumerator UmbralGrotto_Scene_IsDarkAndFogged_WithAColourForEverySurface()
    {
        Scene runner = SceneManager.GetActiveScene();
        yield return SceneManager.LoadSceneAsync(GrottoScene, LoadSceneMode.Additive);
        Scene grotto = SceneManager.GetSceneByName(GrottoScene);
        SceneManager.SetActiveScene(grotto);
        bool isFogged = RenderSettings.fog;
        AmbientMode ambientMode = RenderSettings.ambientMode;
        Color ambient = RenderSettings.ambientLight;
        GameObject[] roots = grotto.GetRootGameObjects();
        Camera camera = roots.Select(root => root.GetComponent<Camera>()).Single(found => found != null);
        CameraClearFlags clearFlags = camera.clearFlags;
        Color background = camera.backgroundColor;
        float sunIntensity = roots.Select(root => root.GetComponent<Light>()).Single(found => found != null).intensity;
        int[] palettes = roots
            .SelectMany(root => root.GetComponentsInChildren<MapLook>())
            .Select(look => look.Palette.Count)
            .ToArray();
        SceneManager.SetActiveScene(runner);
        yield return SceneManager.UnloadSceneAsync(grotto);

        Assert.That(isFogged, Is.True, "fog");
        Assert.That(ambientMode, Is.EqualTo(AmbientMode.Flat));
        Assert.That(ambient.maxColorComponent, Is.LessThan(0.3f), "a dark ambient");
        Assert.That(clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
        Assert.That(background.maxColorComponent, Is.LessThan(0.1f), "a dark background");
        Assert.That(sunIntensity, Is.LessThan(1f), "a dim light");
        Assert.That(palettes, Is.EqualTo(new[] { SurfaceCount }), "one look, a colour for every surface");
    }
}
}
