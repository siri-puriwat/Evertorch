using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Evertorch.Client
{
/// <summary>
///     Click and tap input. It only reports where on the screen the player asked to go; turning that into a walk is
///     the movement controller's decision.
/// </summary>
public sealed class PointerMoveSource : IDisposable
{
    private readonly InputAction m_moveTo;
    private bool m_hasRequest;
    private Vector2 m_requestPosition;

    public PointerMoveSource(InputAction moveTo)
    {
        m_moveTo = moveTo ?? throw new ArgumentNullException(nameof(moveTo));
        m_moveTo.performed += OnMoveTo;
        m_moveTo.Enable();
    }

    public void Dispose()
    {
        m_moveTo.performed -= OnMoveTo;
    }

    public bool TryTakeRequest(out Vector2 screenPosition)
    {
        screenPosition = m_requestPosition;
        bool hadRequest = m_hasRequest;
        m_hasRequest = false;
        return hadRequest;
    }

    private void OnMoveTo(InputAction.CallbackContext context)
    {
        // The position is read from whatever was pressed. A tap belongs to one finger of possibly several, so it
        // comes from that touch. A position action would not do for a mouse either: when the button and the
        // position change in the same event, the action has no value yet at the moment the click is reported.
        if (context.control.parent is TouchControl touch)
        {
            m_requestPosition = touch.position.ReadValue();
        }
        else if (context.control.device is Pointer pointer)
        {
            m_requestPosition = pointer.position.ReadValue();
        }
        else
        {
            return;
        }

        m_hasRequest = true;
    }
}
}
