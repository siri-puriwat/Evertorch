using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
/// Held-direction input. Keyboard, gamepad, and the on-screen stick are all bindings of one action, so this class
/// cannot tell them apart and neither can anything after it.
/// </summary>
public sealed class ManualMoveSource
{
    private readonly InputAction m_move;

    public ManualMoveSource(InputAction move)
    {
        m_move = move ?? throw new ArgumentNullException(nameof(move));
        m_move.Enable();
    }

    /// <param name="cameraYawDegrees">Heading of the camera, so that "up" on the stick walks away from it.</param>
    public void Apply(MovementController controller, float cameraYawDegrees)
    {
        Vector2 stick = m_move.ReadValue<Vector2>();
        float yaw = cameraYawDegrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(yaw);
        float cos = Mathf.Cos(yaw);
        controller.SetManualDirection((stick.x * cos) + (stick.y * sin), (stick.y * cos) - (stick.x * sin));
    }
}
}
