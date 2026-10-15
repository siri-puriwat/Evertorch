using Evertorch.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     Blind darkens the player's own view (Prototype Content §2): a vignette over the world and under the HUD while the
///     local character is blinded, drawn from the state flags.
/// </summary>
public sealed class BlindVignette : MonoBehaviour
{
    // Under the combat HUD and every window, over the world.
    private const int SortingOrder = 4;
    private const int TextureSize = 128;

    // How dark the edge is, and how far from the centre the dark begins, as a share of the half-diagonal.
    private const float EdgeAlpha = 0.85f;
    private const float ClearRadius = 0.35f;

    private GameClient? m_client;
    private GameObject? m_image;
    private Texture2D? m_texture;

    public bool IsShown => m_image != null && m_image.activeSelf;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        UiBuilder.SetActive(
            m_image!,
            world != null && !world.IsLocalDead && (world.LocalStateFlags & EntityStateFlags.Blinded) != 0);
    }

    private void OnDestroy()
    {
        if (m_texture != null)
        {
            Destroy(m_texture);
        }
    }

    public static BlindVignette Create(GameClient client)
    {
        var root = new GameObject("BlindVignette", typeof(RectTransform));
        root.SetActive(false);
        BlindVignette vignette = root.AddComponent<BlindVignette>();
        vignette.Build(client);
        root.SetActive(true);
        return vignette;
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);
        m_texture = CreateTexture();
        m_image = UiBuilder.CreateUiObject("Vignette", transform);
        var rect = (RectTransform)m_image.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        RawImage image = m_image.AddComponent<RawImage>();
        image.texture = m_texture;
        image.raycastTarget = false;
        m_image.SetActive(false);
    }

    // Clear at the centre, darkening smoothly to the corners.
    private static Texture2D CreateTexture()
    {
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "BlindVignette", wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[TextureSize * TextureSize];
        float half = (TextureSize - 1) / 2f;
        float corner = Mathf.Sqrt(2f) * half;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float distance = new Vector2(x - half, y - half).magnitude / corner;
                float dark = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClearRadius, 1f, distance));
                pixels[y * TextureSize + x] = new Color32(0, 0, 0, (byte)(dark * EdgeAlpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
}
