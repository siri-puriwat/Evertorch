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
///     A map's look (Prototype Content §2): the graybox takes its colours from the scene's <see cref="MapLook" />; the
///     Umbral Grotto's scene is dark and fogged, and the Ashen Quarry's warm and fogged, each with a colour for every
///     surface.
/// </summary>
public sealed class MapLookTests
{
    private const string GrottoScene = "12_UmbralGrotto";
    private const string QuarryScene = "13_AshenQuarry";

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

    // WCAG's contrast ratio of two colours' relative luminances.
    private static float Contrast(Color first, Color second)
    {
        float lighter = Mathf.Max(Luminance(first), Luminance(second));
        float darker = Mathf.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05f) / (darker + 0.05f);
    }

    private static float Luminance(Color colour)
    {
        Color linear = colour.linear;
        return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
    }

    private static float Saturation(Color colour)
    {
        Color.RGBToHSV(colour, out float _, out float saturation, out float _);
        return saturation;
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

    // Owner decision 20: a warm, dim light, a warm flat ambient, and a warm fog over ash floors and charcoal walls. The
    // area telegraph's ring is drawn unlit (Gameplay Systems §9), so it must read on the floor by its luminance and its
    // hue alike.
    [UnityTest]
    public IEnumerator AshenQuarry_Scene_IsWarmAndFogged_AndTheTelegraphReadsOnItsFloor()
    {
        Scene runner = SceneManager.GetActiveScene();
        yield return SceneManager.LoadSceneAsync(QuarryScene, LoadSceneMode.Additive);
        Scene quarry = SceneManager.GetSceneByName(QuarryScene);
        SceneManager.SetActiveScene(quarry);
        bool isFogged = RenderSettings.fog;
        Color fog = RenderSettings.fogColor;
        AmbientMode ambientMode = RenderSettings.ambientMode;
        Color ambient = RenderSettings.ambientLight;
        GameObject[] roots = quarry.GetRootGameObjects();
        Light sun = roots.Select(root => root.GetComponent<Light>()).Single(found => found != null);
        Color sunColour = sun.color;
        float sunIntensity = sun.intensity;
        Vector3 towardTheSun = -sun.transform.forward;
        IReadOnlyList<Color>[] palettes = roots
            .SelectMany(root => root.GetComponentsInChildren<MapLook>())
            .Select(look => look.Palette)
            .ToArray();
        SceneManager.SetActiveScene(runner);
        yield return SceneManager.UnloadSceneAsync(quarry);

        Assert.That(isFogged, Is.True, "fog");
        Assert.That(ambientMode, Is.EqualTo(AmbientMode.Flat));
        Assert.That(
            new[] { fog, ambient, sunColour }.Select(colour => colour.r > colour.b),
            Is.All.True,
            "a warm fog, ambient, and light");
        Assert.That(sunIntensity, Is.LessThan(1f), "a dim light");
        Assert.That(palettes.Select(palette => palette.Count), Is.EqualTo(new[] { SurfaceCount }), "one look");
        // The floor as the camera sees it: its colour lit diffusely by the flat ambient and the sun, in linear light;
        // the ring is unlit and shows its own colour.
        Color floor = palettes[0][(int)GrayboxSurface.Floor];
        float facing = Mathf.Max(0f, Vector3.Dot(Vector3.up, towardTheSun));
        Color light = ambient.linear + sunColour.linear * sunIntensity * facing;
        Color litFloor = new Color(
                floor.linear.r * light.r,
                floor.linear.g * light.g,
                floor.linear.b * light.b)
            .gamma;
        Assert.That(Contrast(AreaTelegraphPresenter.RingColor, litFloor), Is.GreaterThanOrEqualTo(1.8f), "luminance");
        Assert.That(
            Saturation(AreaTelegraphPresenter.RingColor) - Saturation(litFloor),
            Is.GreaterThanOrEqualTo(0.25f),
            "a vivid ring on warm-lit ash");
    }
}
}
