using System;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     Target cycling and clearing, attacking, respawning, picking up, and talking from the keyboard and the gamepad
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
    private readonly InputAction m_talk;
    private CombatRequest m_request;
    private bool m_isFromGamepad;

    public CombatInputSource(
        InputAction next,
        InputAction previous,
        InputAction clear,
        InputAction attack,
        InputAction respawn,
        InputAction pickup,
        InputAction talk)
    {
        m_next = next ?? throw new ArgumentNullException(nameof(next));
        m_previous = previous ?? throw new ArgumentNullException(nameof(previous));
        m_clear = clear ?? throw new ArgumentNullException(nameof(clear));
        m_attack = attack ?? throw new ArgumentNullException(nameof(attack));
        m_respawn = respawn ?? throw new ArgumentNullException(nameof(respawn));
        m_pickup = pickup ?? throw new ArgumentNullException(nameof(pickup));
        m_talk = talk ?? throw new ArgumentNullException(nameof(talk));
        m_next.performed += OnNext;
        m_previous.performed += OnPrevious;
        m_clear.performed += OnClear;
        m_attack.performed += OnAttack;
        m_respawn.performed += OnRespawn;
        m_pickup.performed += OnPickup;
        m_talk.performed += OnTalk;
        m_next.Enable();
        m_previous.Enable();
        m_clear.Enable();
        m_attack.Enable();
        m_respawn.Enable();
        m_pickup.Enable();
        m_talk.Enable();
    }

    public void Dispose()
    {
        m_next.performed -= OnNext;
        m_previous.performed -= OnPrevious;
        m_clear.performed -= OnClear;
        m_attack.performed -= OnAttack;
        m_respawn.performed -= OnRespawn;
        m_pickup.performed -= OnPickup;
        m_talk.performed -= OnTalk;
    }

    public CombatRequest TakeRequest()
    {
        return TakeRequest(out bool _);
    }

    /// <summary>
    ///     The request since the last call; <paramref name="isFromGamepad" /> says whether a gamepad asked, since the
    ///     d-pad's right is slot 9 on the gamepad's second page (Prototype Content §4).
    /// </summary>
    public CombatRequest TakeRequest(out bool isFromGamepad)
    {
        CombatRequest request = m_request;
        isFromGamepad = request != CombatRequest.None && m_isFromGamepad;
        m_request = CombatRequest.None;
        m_isFromGamepad = false;
        return request;
    }

    private void Set(CombatRequest request, InputAction.CallbackContext context)
    {
        m_request = request;
        m_isFromGamepad = context.control?.device is Gamepad;
    }

    private void OnNext(InputAction.CallbackContext context)
    {
        // Tab on its own is Next and Shift+Tab is Previous; the plain Tab binding also fires with Shift held.
        if (context.control.device is Keyboard keyboard && keyboard.shiftKey.isPressed)
        {
            return;
        }

        Set(CombatRequest.Next, context);
    }

    private void OnPrevious(InputAction.CallbackContext context)
    {
        Set(CombatRequest.Previous, context);
    }

    private void OnClear(InputAction.CallbackContext context)
    {
        Set(CombatRequest.Clear, context);
    }

    private void OnAttack(InputAction.CallbackContext context)
    {
        Set(CombatRequest.Attack, context);
    }

    private void OnRespawn(InputAction.CallbackContext context)
    {
        Set(CombatRequest.Respawn, context);
    }

    private void OnPickup(InputAction.CallbackContext context)
    {
        Set(CombatRequest.Pickup, context);
    }

    private void OnTalk(InputAction.CallbackContext context)
    {
        Set(CombatRequest.Talk, context);
    }
}
}
