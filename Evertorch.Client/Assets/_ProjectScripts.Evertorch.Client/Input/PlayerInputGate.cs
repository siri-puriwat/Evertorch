using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     Shuts the Player action map while any input field has focus (Prototype Content §4), so no gameplay key acts on
///     what is typed, and opens again exactly the actions it shut once no field has it. Each field holds the gate on its
///     own, so one losing focus never opens it under another.
/// </summary>
public sealed class PlayerInputGate
{
    private const string PlayerMap = "Player";

    private readonly InputActionMap? m_map;
    private readonly List<InputAction> m_shut = new();
    private readonly HashSet<object> m_holders = new();

    public PlayerInputGate(InputActionAsset? actions)
    {
        m_map = actions != null ? actions.FindActionMap(PlayerMap) : null;
    }

    public bool IsShut => m_holders.Count > 0;

    /// <summary>
    ///     How many fields hold the gate shut.
    /// </summary>
    public int Holders => m_holders.Count;

    public void Shut(object holder)
    {
        if (!m_holders.Add(holder) || m_holders.Count > 1 || m_map == null)
        {
            return;
        }

        foreach (InputAction action in m_map.actions)
        {
            if (action.enabled)
            {
                m_shut.Add(action);
                action.Disable();
            }
        }
    }

    public void Open(object holder)
    {
        if (!m_holders.Remove(holder) || m_holders.Count > 0)
        {
            return;
        }

        foreach (InputAction action in m_shut)
        {
            action.Enable();
        }

        m_shut.Clear();
    }
}
}
