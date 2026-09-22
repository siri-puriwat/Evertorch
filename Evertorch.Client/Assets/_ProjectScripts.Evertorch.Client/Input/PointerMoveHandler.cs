using System;
using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     Turns a pending click or tap into a walk request. A position on a control is never also a position on the
///     ground, and the movement controller alone decides whether the point can be walked to.
/// </summary>
public sealed class PointerMoveHandler
{
    private readonly PointerMoveSource m_source;
    private readonly TouchControls? m_touch;
    private readonly DevelopmentOverlay? m_overlay;

    public PointerMoveHandler(PointerMoveSource source, TouchControls? touch, DevelopmentOverlay? overlay)
    {
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        m_touch = touch;
        m_overlay = overlay;
    }

    /// <param name="point">Where on the map the request landed, when it reached the map at all.</param>
    public PointerMoveResult Handle(
        Camera? camera,
        Collider? ground,
        MovementController controller,
        WorldPosition from,
        out WorldPosition point)
    {
        point = default;
        if (!m_source.TryTakeRequest(out Vector2 screenPosition))
        {
            return PointerMoveResult.None;
        }

        bool isOnControl = (m_touch != null && m_touch.IsOverControl(screenPosition))
            || (m_overlay != null && m_overlay.Covers(screenPosition));
        if (isOnControl)
        {
            return PointerMoveResult.OnControl;
        }

        if (camera == null || ground == null || !GroundPicker.TryPick(camera, ground, screenPosition, out point))
        {
            return PointerMoveResult.MissedMap;
        }

        return controller.TryMoveTo(from, point) ? PointerMoveResult.Accepted : PointerMoveResult.Refused;
    }
}
}
