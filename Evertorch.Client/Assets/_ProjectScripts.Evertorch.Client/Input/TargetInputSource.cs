using System;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     Target cycling and clearing from the keyboard and the gamepad (Prototype Content §4). It only reports what was
///     asked for; the request goes to the server, which decides.
/// </summary>
public sealed class TargetInputSource : IDisposable
{
    private readonly InputAction m_next;
    private readonly InputAction m_previous;
    private readonly InputAction m_clear;
    private readonly InputAction m_attack;
    private TargetRequest m_request;

    public TargetInputSource(InputAction next, InputAction previous, InputAction clear, InputAction attack)
    {
        m_next = next ?? throw new ArgumentNullException(nameof(next));
        m_previous = previous ?? throw new ArgumentNullException(nameof(previous));
        m_clear = clear ?? throw new ArgumentNullException(nameof(clear));
        m_attack = attack ?? throw new ArgumentNullException(nameof(attack));
        m_next.performed += OnNext;
        m_previous.performed += OnPrevious;
        m_clear.performed += OnClear;
        m_attack.performed += OnAttack;
        m_next.Enable();
        m_previous.Enable();
        m_clear.Enable();
        m_attack.Enable();
    }

    public void Dispose()
    {
        m_next.performed -= OnNext;
        m_previous.performed -= OnPrevious;
        m_clear.performed -= OnClear;
        m_attack.performed -= OnAttack;
    }

    public TargetRequest TakeRequest()
    {
        TargetRequest request = m_request;
        m_request = TargetRequest.None;
        return request;
    }

    private void OnNext(InputAction.CallbackContext context)
    {
        // Tab on its own is Next and Shift+Tab is Previous; the plain Tab binding also fires with Shift held.
        if (context.control.device is Keyboard keyboard && keyboard.shiftKey.isPressed)
        {
            return;
        }

        m_request = TargetRequest.Next;
    }

    private void OnPrevious(InputAction.CallbackContext context)
    {
        m_request = TargetRequest.Previous;
    }

    private void OnClear(InputAction.CallbackContext context)
    {
        m_request = TargetRequest.Clear;
    }

    private void OnAttack(InputAction.CallbackContext context)
    {
        m_request = TargetRequest.Attack;
    }
}
}
