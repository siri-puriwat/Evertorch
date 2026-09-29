using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     The crosshair the pointer becomes while a skill waits for a click on its target (Prototype Content §4). It is drawn
///     at run time, so the graybox needs no cursor art; only a hardware cursor changes, and a touch screen has none.
/// </summary>
public sealed class TargetCursor : IDisposable
{
    private const int Size = 32;
    private const float RingInner = 8f;
    private const float RingOuter = 10f;
    private const float TickStart = 4f;
    private const float TickEnd = 14f;

    private static readonly Color32 Light = new(255, 240, 150, 255);
    private static readonly Color32 Dark = new(20, 20, 20, 255);

    private Texture2D? m_texture;

    public bool IsShown { get; private set; }

    public void Dispose()
    {
        SetShown(false);
        if (m_texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(m_texture);
        }
        else
        {
            Object.DestroyImmediate(m_texture);
        }

        m_texture = null;
    }

    /// <summary>
    ///     Shows the crosshair in place of the pointer, or the default pointer again; only a change reaches Unity.
    /// </summary>
    public void SetShown(bool isShown)
    {
        if (isShown == IsShown)
        {
            return;
        }

        IsShown = isShown;
        if (!isShown)
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        // A destroyed texture compares equal to null only through Unity's own operator, never through ??=.
        if (m_texture == null)
        {
            m_texture = Draw();
        }

        Cursor.SetCursor(m_texture, new Vector2(Size / 2f, Size / 2f), CursorMode.Auto);
    }

    // A light ring with four ticks pointing in at a clear centre, outlined in dark so it reads on grass and on stone. The
    // texture stays readable and without mipmaps, which a cursor needs.
    private static Texture2D Draw()
    {
        bool[] isLight = new bool[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                isLight[y * Size + x] = IsOnCrosshair(x, y);
            }
        }

        var pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                if (isLight[index])
                {
                    pixels[index] = Light;
                }
                else if (TouchesLight(isLight, x, y))
                {
                    pixels[index] = Dark;
                }
            }
        }

        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "TargetCursor",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
#if UNITY_EDITOR
        texture.alphaIsTransparency = true;
#endif
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static bool IsOnCrosshair(int x, int y)
    {
        float dx = x + 0.5f - Size / 2f;
        float dy = y + 0.5f - Size / 2f;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);
        bool isRing = distance >= RingInner && distance <= RingOuter;
        bool isVerticalTick = Mathf.Abs(dx) < 1f && Mathf.Abs(dy) >= TickStart && Mathf.Abs(dy) <= TickEnd;
        bool isHorizontalTick = Mathf.Abs(dy) < 1f && Mathf.Abs(dx) >= TickStart && Mathf.Abs(dx) <= TickEnd;
        return isRing || isVerticalTick || isHorizontalTick;
    }

    private static bool TouchesLight(bool[] isLight, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < Size && ny >= 0 && ny < Size && isLight[ny * Size + nx])
                {
                    return true;
                }
            }
        }

        return false;
    }
}
}
