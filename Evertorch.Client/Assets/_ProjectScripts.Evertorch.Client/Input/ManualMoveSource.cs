using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     Held-direction input. Keyboard, gamepad, and the on-screen stick are all bindings of one action, so this class
///     cannot tell them apart and neither can anything after it.
/// </summary>
public sealed class ManualMoveSource
{
    // Below a tenth of the tilt the stick counts as centred; the gamepad's dead zone already reads less as nothing.
    private const float CentredSquared = 0.01f;

    private readonly InputAction m_move;
    private bool m_isCentreRequired;

    public ManualMoveSource(InputAction move)
    {
        m_move = move ?? throw new ArgumentNullException(nameof(move));
        m_move.Enable();
    }

    /// <summary>
    ///     Ignores the held direction until it is centred, as after the gamepad's focus on a window (Prototype Content
    ///     §4): a stick still pushed when the focus ends walks nowhere.
    /// </summary>
    public void RequireCentre()
    {
        m_isCentreRequired = true;
    }

    /// <param name="cameraYawDegrees">Heading of the camera, so that "up" on the stick walks away from it.</param>
    public void Apply(MovementController controller, float cameraYawDegrees)
    {
        Vector2 stick = m_move.ReadValue<Vector2>();
        if (m_isCentreRequired)
        {
            if (stick.sqrMagnitude >= CentredSquared)
            {
                stick = Vector2.zero;
            }
            else
            {
                m_isCentreRequired = false;
            }
        }

        float yaw = cameraYawDegrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(yaw);
        float cos = Mathf.Cos(yaw);
        controller.SetManualDirection(stick.x * cos + stick.y * sin, stick.y * cos - stick.x * sin);
    }
}
}
