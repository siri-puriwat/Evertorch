using System;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     The window keys (Prototype Content §4): C, the gamepad's Select, or the Stats touch button, which drives Select,
///     open or close the Stats window. It only reports the press; the client decides what it opens.
/// </summary>
public sealed class WindowInputSource : IDisposable
{
    private readonly InputAction m_stats;
    private bool m_isStatsPressed;

    public WindowInputSource(InputAction stats)
    {
        m_stats = stats ?? throw new ArgumentNullException(nameof(stats));
        m_stats.performed += OnStats;
        m_stats.Enable();
    }

    public void Dispose()
    {
        m_stats.performed -= OnStats;
    }

    public bool TakeStatsToggle()
    {
        bool isPressed = m_isStatsPressed;
        m_isStatsPressed = false;
        return isPressed;
    }

    private void OnStats(InputAction.CallbackContext context)
    {
        m_isStatsPressed = true;
    }
}
}
