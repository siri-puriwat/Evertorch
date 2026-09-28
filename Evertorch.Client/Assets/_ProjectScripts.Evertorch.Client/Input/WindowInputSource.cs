using System;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     The window keys (Prototype Content §4): C, the gamepad's Select, or the Stats touch button, which drives Select,
///     open or close the Stats window; K, a press of the left stick, or the Skills touch button, which drives that
///     press, the Skills window. It only reports the presses; the client decides what they open.
/// </summary>
public sealed class WindowInputSource : IDisposable
{
    private readonly InputAction m_stats;
    private readonly InputAction m_skills;
    private bool m_isStatsPressed;
    private bool m_isSkillsPressed;

    public WindowInputSource(InputAction stats, InputAction skills)
    {
        m_stats = stats ?? throw new ArgumentNullException(nameof(stats));
        m_skills = skills ?? throw new ArgumentNullException(nameof(skills));
        m_stats.performed += OnStats;
        m_skills.performed += OnSkills;
        m_stats.Enable();
        m_skills.Enable();
    }

    public void Dispose()
    {
        m_stats.performed -= OnStats;
        m_skills.performed -= OnSkills;
    }

    public bool TakeSkillsToggle()
    {
        bool isPressed = m_isSkillsPressed;
        m_isSkillsPressed = false;
        return isPressed;
    }

    public bool TakeStatsToggle()
    {
        bool isPressed = m_isStatsPressed;
        m_isStatsPressed = false;
        return isPressed;
    }

    private void OnSkills(InputAction.CallbackContext context)
    {
        m_isSkillsPressed = true;
    }

    private void OnStats(InputAction.CallbackContext context)
    {
        m_isStatsPressed = true;
    }
}
}
