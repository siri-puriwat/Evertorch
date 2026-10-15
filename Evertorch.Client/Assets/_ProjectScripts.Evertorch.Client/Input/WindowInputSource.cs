using System;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     The window keys (Prototype Content §4): C opens or closes the Stats window; K, a press of the left stick, or the
///     Skills touch button, which drives that press, the Skills window; I the Equipment window; and the gamepad's Select,
///     or the Menu touch button, which drives Select, the Menu. Each action binds alone, so a missing one silences only
///     itself. It only reports the presses; the client decides what they open.
/// </summary>
public sealed class WindowInputSource : IDisposable
{
    private readonly InputAction? m_stats;
    private readonly InputAction? m_skills;
    private readonly InputAction? m_menu;
    private readonly InputAction? m_equipment;
    private bool m_isStatsPressed;
    private bool m_isSkillsPressed;
    private bool m_isMenuPressed;
    private bool m_isEquipmentPressed;

    public WindowInputSource(InputAction? stats, InputAction? skills, InputAction? menu, InputAction? equipment)
    {
        m_stats = stats;
        m_skills = skills;
        m_menu = menu;
        m_equipment = equipment;
        Bind(m_stats, OnStats);
        Bind(m_skills, OnSkills);
        Bind(m_menu, OnMenu);
        Bind(m_equipment, OnEquipment);
    }

    public void Dispose()
    {
        Unbind(m_stats, OnStats);
        Unbind(m_skills, OnSkills);
        Unbind(m_menu, OnMenu);
        Unbind(m_equipment, OnEquipment);
    }

    public bool TakeStatsToggle()
    {
        bool isPressed = m_isStatsPressed;
        m_isStatsPressed = false;
        return isPressed;
    }

    public bool TakeSkillsToggle()
    {
        bool isPressed = m_isSkillsPressed;
        m_isSkillsPressed = false;
        return isPressed;
    }

    public bool TakeMenuToggle()
    {
        bool isPressed = m_isMenuPressed;
        m_isMenuPressed = false;
        return isPressed;
    }

    public bool TakeEquipmentToggle()
    {
        bool isPressed = m_isEquipmentPressed;
        m_isEquipmentPressed = false;
        return isPressed;
    }

    /// <summary>
    ///     Forgets every press not yet taken.
    /// </summary>
    public void Drop()
    {
        m_isStatsPressed = false;
        m_isSkillsPressed = false;
        m_isMenuPressed = false;
        m_isEquipmentPressed = false;
    }

    private static void Bind(InputAction? action, Action<InputAction.CallbackContext> onPerformed)
    {
        if (action != null)
        {
            action.performed += onPerformed;
            action.Enable();
        }
    }

    private static void Unbind(InputAction? action, Action<InputAction.CallbackContext> onPerformed)
    {
        if (action != null)
        {
            action.performed -= onPerformed;
        }
    }

    private void OnStats(InputAction.CallbackContext context)
    {
        m_isStatsPressed = true;
    }

    private void OnSkills(InputAction.CallbackContext context)
    {
        m_isSkillsPressed = true;
    }

    private void OnMenu(InputAction.CallbackContext context)
    {
        m_isMenuPressed = true;
    }

    private void OnEquipment(InputAction.CallbackContext context)
    {
        m_isEquipmentPressed = true;
    }
}
}
