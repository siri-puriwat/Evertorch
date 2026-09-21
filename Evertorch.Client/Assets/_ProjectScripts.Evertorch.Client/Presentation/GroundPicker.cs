using Evertorch.Game;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
/// Finds the point on the map under a screen position.
/// </summary>
public static class GroundPicker
{
    private const float MaxDistance = 500f;

    public static bool TryPick(Camera camera, Collider ground, Vector2 screenPosition, out WorldPosition point)
    {
        Ray ray = camera.ScreenPointToRay(screenPosition);
        if (ground.Raycast(ray, out RaycastHit hit, MaxDistance))
        {
            point = new WorldPosition(hit.point.x, hit.point.y, hit.point.z);
            return true;
        }

        point = default;
        return false;
    }
}
}
