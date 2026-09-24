using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Evertorch.Client
{
/// <summary>
///     The one test of whether a screen position is on the client's UI (Gameplay Systems §5.1): the event system's
///     raycast over every client canvas, so a click or tap on any panel is never also one in the world. Text that lets
///     clicks through sets <c>raycastTarget</c> to false.
/// </summary>
public sealed class UiHitTest
{
    private readonly List<RaycastResult> m_results = new();

    public bool IsOverUi(Vector2 screenPosition)
    {
        EventSystem? eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        var pointer = new PointerEventData(eventSystem) { position = screenPosition };
        m_results.Clear();
        eventSystem.RaycastAll(pointer, m_results);
        return m_results.Count > 0;
    }
}
}
