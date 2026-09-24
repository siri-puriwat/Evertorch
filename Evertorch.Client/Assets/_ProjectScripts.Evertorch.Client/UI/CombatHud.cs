using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The way back after dying: a Respawn button in the middle of the screen (the touch path of Prototype Content
///     §4) beside the R and Start bindings. The HP readout is part of the <see cref="StatusBar" />.
/// </summary>
public sealed class CombatHud : MonoBehaviour
{
    // Under the status bar and the other panels, over the on-screen stick.
    private const int SortingOrder = 5;
    private const float FontSize = 40f;
    private static readonly Vector2 ButtonSize = new(360f, 110f);
    private static readonly Color TextColor = new(0.95f, 0.95f, 0.97f);
    private static readonly Color ButtonColor = new(0.25f, 0.65f, 0.95f, 0.95f);

    // A pressed button is never left selected: UI navigation shares WASD and the stick with movement.
    private static readonly Navigation NoNavigation = new() { mode = Navigation.Mode.None };

    private GameClient? m_client;
    private GameObject? m_respawn;

    public bool IsRespawnShown => m_respawn != null && m_respawn.activeSelf;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        if (m_respawn != null)
        {
            UiBuilder.SetActive(m_respawn, world != null && world.IsLocalDead);
        }
    }

    public static CombatHud Create(GameClient client)
    {
        // Built inactive, so the button is fully wired before it is enabled.
        var root = new GameObject("CombatHud", typeof(RectTransform));
        root.SetActive(false);
        CombatHud hud = root.AddComponent<CombatHud>();
        hud.m_client = client;
        ClientUI.EnsureEventSystem(root.transform);
        ClientUI.AddScreenCanvas(root, SortingOrder);
        hud.m_respawn = CreateRespawnButton(root.transform, client);
        hud.m_respawn.SetActive(false);
        root.SetActive(true);
        return hud;
    }

    private static GameObject CreateRespawnButton(Transform parent, GameClient client)
    {
        var buttonObject = new GameObject("Respawn", typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);
        var rect = (RectTransform)buttonObject.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = ButtonSize;
        buttonObject.AddComponent<Image>().color = ButtonColor;
        Button button = buttonObject.AddComponent<Button>();
        button.navigation = NoNavigation;
        button.onClick.AddListener(client.RequestRespawn);

        var textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(buttonObject.transform, false);
        var textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = "Respawn";
        text.fontSize = FontSize;
        text.color = TextColor;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return buttonObject;
    }
}
}
