using System;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     Target cycling and clearing, attacking, respawning, and picking up from the keyboard and the gamepad
///     (Prototype Content §4). It only reports what was asked for; the request goes to the server, which decides.
/// </summary>
public sealed class CombatInputSource : IDisposable
{
    private readonly InputAction m_next;
    private readonly InputAction m_previous;
    private readonly InputAction m_clear;
    private readonly InputAction m_attack;
    private readonly InputAction m_respawn;
    private readonly InputAction m_pickup;
    private CombatRequest m_request;

    public CombatInputSource(
        InputAction next,
        InputAction previous,
        InputAction clear,
        InputAction attack,
        InputAction respawn,
        InputAction pickup)
    {
        m_next = next ?? throw new ArgumentNullException(nameof(next));
        m_previous = previous ?? throw new ArgumentNullException(nameof(previous));
        m_clear = clear ?? throw new ArgumentNullException(nameof(clear));
        m_attack = attack ?? throw new ArgumentNullException(nameof(attack));
        m_respawn = respawn ?? throw new ArgumentNullException(nameof(respawn));
        m_pickup = pickup ?? throw new ArgumentNullException(nameof(pickup));
        m_next.performed += OnNext;
        m_previous.performed += OnPrevious;
        m_clear.performed += OnClear;
        m_attack.performed += OnAttack;
        m_respawn.performed += OnRespawn;
        m_pickup.performed += OnPickup;
        m_next.Enable();
        m_previous.Enable();
        m_clear.Enable();
        m_attack.Enable();
        m_respawn.Enable();
        m_pickup.Enable();
    }

    public void Dispose()
    {
        m_next.performed -= OnNext;
        m_previous.performed -= OnPrevious;
        m_clear.performed -= OnClear;
        m_attack.performed -= OnAttack;
        m_respawn.performed -= OnRespawn;
        m_pickup.performed -= OnPickup;
    }

    public CombatRequest TakeRequest()
    {
        CombatRequest request = m_request;
        m_request = CombatRequest.None;
        return request;
    }

    private void OnNext(InputAction.CallbackContext context)
    {
        // Tab on its own is Next and Shift+Tab is Previous; the plain Tab binding also fires with Shift held.
        if (context.control.device is Keyboard keyboard && keyboard.shiftKey.isPressed)
        {
            return;
        }

        m_request = CombatRequest.Next;
    }

    private void OnPrevious(InputAction.CallbackContext context)
    {
        m_request = CombatRequest.Previous;
    }

    private void OnClear(InputAction.CallbackContext context)
    {
        m_request = CombatRequest.Clear;
    }

    private void OnAttack(InputAction.CallbackContext context)
    {
        m_request = CombatRequest.Attack;
    }

    private void OnRespawn(InputAction.CallbackContext context)
    {
        m_request = CombatRequest.Respawn;
    }

    private void OnPickup(InputAction.CallbackContext context)
    {
        m_request = CombatRequest.Pickup;
    }
}
}
