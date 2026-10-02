using System;
using System.Collections.Generic;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A map scene's colours for the graybox drawn from the map's grid, one per <see cref="GrayboxSurface" /> in its
///     order (Prototype Content §2). The scene's light, ambient, and fog set the rest of its look; a scene without one
///     keeps the default colours.
/// </summary>
public sealed class MapLook : MonoBehaviour
{
    [SerializeField]
    private Color[] m_palette = Array.Empty<Color>();

    public IReadOnlyList<Color> Palette => m_palette;
}
}
