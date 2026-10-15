using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     The gamepad's focus on the Menu or the Equipment window (Prototype Content §4), read from the Menu action map:
///     the d-pad or the left stick moves a highlight a step per push, South chooses, East backs out one level, and Select
///     ends focus. The map is enabled only while focus holds; a press counts once, and a stick held when focus begins
///     moves nothing until it is centred.
/// </summary>
public sealed class MenuInputSource : IDisposable
{
    public const string MapName = "Menu";

    // A push past half the tilt steps once; the stick steps again only after falling back under a quarter.
    private const float StepTilt = 0.5f;
    private const float CentredTilt = 0.25f;

    private readonly InputActionMap m_map;
    private readonly InputAction m_navigate;
    private readonly InputAction m_choose;
    private readonly InputAction m_back;
    private readonly InputAction m_end;
    private bool m_isChosen;
    private bool m_isBacked;
    private bool m_isEnded;
    private bool m_isPushed;

    public MenuInputSource(InputActionMap map)
    {
        m_map = map ?? throw new ArgumentNullException(nameof(map));
        m_navigate = map.FindAction("Navigate", true);
        m_choose = map.FindAction("Choose", true);
        m_back = map.FindAction("Back", true);
        m_end = map.FindAction("End", true);
        m_choose.performed += OnChoose;
        m_back.performed += OnBack;
        m_end.performed += OnEnd;

        // The Input System enables every map of the project-wide asset at play, this one too.
        m_map.Disable();
    }

    public bool IsActive => m_map.enabled;

    public void Dispose()
    {
        m_choose.performed -= OnChoose;
        m_back.performed -= OnBack;
        m_end.performed -= OnEnd;
        m_map.Disable();
    }

    /// <summary>
    ///     The source over the Menu map of <paramref name="actions" />; null when the map or one of its actions is
    ///     missing.
    /// </summary>
    public static MenuInputSource? TryCreate(InputActionAsset? actions)
    {
        InputActionMap? map = actions != null ? actions.FindActionMap(MapName) : null;
        if (map == null
            || map.FindAction("Navigate") == null
            || map.FindAction("Choose") == null
            || map.FindAction("Back") == null
            || map.FindAction("End") == null)
        {
            return null;
        }

        return new MenuInputSource(map);
    }

    public void Begin()
    {
        Drop();
        m_isPushed = true;
        m_map.Enable();
    }

    public void End()
    {
        m_map.Disable();
        Drop();
    }

    /// <summary>
    ///     -1 for a push up, 1 for a push down, once per push; 0 otherwise.
    /// </summary>
    public int TakeStep()
    {
        if (!m_map.enabled)
        {
            return 0;
        }

        float tilt = m_navigate.ReadValue<Vector2>().y;
        if (m_isPushed)
        {
            m_isPushed = Mathf.Abs(tilt) >= CentredTilt;
            return 0;
        }

        if (Mathf.Abs(tilt) < StepTilt)
        {
            return 0;
        }

        m_isPushed = true;
        return tilt > 0f ? -1 : 1;
    }

    public bool TakeChoose()
    {
        bool isChosen = m_isChosen;
        m_isChosen = false;
        return isChosen;
    }

    public bool TakeBack()
    {
        bool isBacked = m_isBacked;
        m_isBacked = false;
        return isBacked;
    }

    public bool TakeEnd()
    {
        bool isEnded = m_isEnded;
        m_isEnded = false;
        return isEnded;
    }

    private void Drop()
    {
        m_isChosen = false;
        m_isBacked = false;
        m_isEnded = false;
    }

    private void OnChoose(InputAction.CallbackContext context)
    {
        m_isChosen = true;
    }

    private void OnBack(InputAction.CallbackContext context)
    {
        m_isBacked = true;
    }

    private void OnEnd(InputAction.CallbackContext context)
    {
        m_isEnded = true;
    }
}
}
