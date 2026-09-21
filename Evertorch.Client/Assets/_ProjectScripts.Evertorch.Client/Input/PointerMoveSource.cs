using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Evertorch.Client
{
/// <summary>
/// Click and tap input. It only reports where on the screen the player asked to go; turning that into a walk is
/// the movement controller's decision.
/// </summary>
public sealed class PointerMoveSource : IDisposable
{
    private readonly InputAction m_moveTo;
    private readonly InputAction m_pointerPosition;
    private bool m_hasRequest;
    private Vector2 m_requestPosition;

    public PointerMoveSource(InputAction moveTo, InputAction pointerPosition)
    {
        m_moveTo = moveTo ?? throw new ArgumentNullException(nameof(moveTo));
        m_pointerPosition = pointerPosition ?? throw new ArgumentNullException(nameof(pointerPosition));
        m_moveTo.performed += OnMoveTo;
        m_moveTo.Enable();
        m_pointerPosition.Enable();
    }

    public bool TryTakeRequest(out Vector2 screenPosition)
    {
        screenPosition = m_requestPosition;
        bool hadRequest = m_hasRequest;
        m_hasRequest = false;
        return hadRequest;
    }

    public void Dispose()
    {
        m_moveTo.performed -= OnMoveTo;
    }

    private void OnMoveTo(InputAction.CallbackContext context)
    {
        // A tap belongs to one finger of possibly several, so its position comes from that touch rather than from
        // a shared pointer position.
        m_requestPosition = context.control.parent is TouchControl touch
            ? touch.position.ReadValue()
            : m_pointerPosition.ReadValue<Vector2>();
        m_hasRequest = true;
    }
}
}
