using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     Setup every client canvas shares, so that all screen-space UI scales the same way.
/// </summary>
public static class ClientUI
{
    private static readonly Vector2 ReferenceResolution = new(1080f, 1920f);

    /// <summary>
    ///     The event system every client canvas shares, created under <paramref name="owner" /> when there is none yet.
    /// </summary>
    public static EventSystem EnsureEventSystem(Transform owner)
    {
        EventSystem? current = EventSystem.current;
        if (current != null)
        {
            return current;
        }

        var eventSystem = new GameObject("EventSystem");
        eventSystem.transform.SetParent(owner, false);
        current = eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        return current;
    }

    public static Canvas AddScreenCanvas(GameObject target, int sortingOrder)
    {
        Canvas canvas = target.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = target.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        target.AddComponent<GraphicRaycaster>();
        return canvas;
    }
}
}
