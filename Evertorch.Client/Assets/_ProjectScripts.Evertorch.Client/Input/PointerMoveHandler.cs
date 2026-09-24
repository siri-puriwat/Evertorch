using System;
using System.Collections.Generic;
using Evertorch.Game;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;

namespace Evertorch.Client
{
/// <summary>
///     Turns a pending click or tap into a request: the entity under it when there is one, otherwise a walk. A
///     position on the UI is never also a position in the world, and the movement controller alone decides whether a
///     point can be walked to.
/// </summary>
public sealed class PointerMoveHandler
{
    private readonly PointerMoveSource m_source;
    private readonly UiHitTest? m_ui;

    public PointerMoveHandler(PointerMoveSource source, UiHitTest? ui)
    {
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        m_ui = ui;
    }

    /// <param name="entities">What a click may pick; tested before the ground, so the entity under it wins.</param>
    /// <param name="point">Where on the map the request landed, when it reached the map at all.</param>
    /// <param name="entity">The picked entity when the result is <see cref="PointerMoveResult.Entity" />.</param>
    public PointerMoveResult Handle(
        Camera? camera,
        Collider? ground,
        IReadOnlyList<PickCandidate> entities,
        MovementController controller,
        WorldPosition from,
        out WorldPosition point,
        out EntityId entity)
    {
        point = default;
        entity = default;
        if (!m_source.TryTakeRequest(out Vector2 screenPosition))
        {
            return PointerMoveResult.None;
        }

        if (m_ui != null && m_ui.IsOverUi(screenPosition))
        {
            return PointerMoveResult.OnControl;
        }

        if (camera != null && entities.Count > 0)
        {
            Ray ray = camera.ScreenPointToRay(screenPosition);
            var origin = new WorldPosition(ray.origin.x, ray.origin.y, ray.origin.z);
            if (EntityPicker.TryPick(origin, ray.direction.x, ray.direction.y, ray.direction.z, entities, out entity))
            {
                return PointerMoveResult.Entity;
            }
        }

        if (camera == null || ground == null || !GroundPicker.TryPick(camera, ground, screenPosition, out point))
        {
            return PointerMoveResult.MissedMap;
        }

        return controller.TryMoveTo(from, point) ? PointerMoveResult.Accepted : PointerMoveResult.Refused;
    }
}
}
