using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     Shuts the Player action map while the chat input has focus (Prototype Content §4), so no gameplay key acts on
///     what is typed, and opens again exactly the actions it shut.
/// </summary>
public sealed class PlayerInputGate
{
    private const string PlayerMap = "Player";

    private readonly InputActionMap? m_map;
    private readonly List<InputAction> m_shut = new();

    public PlayerInputGate(InputActionAsset? actions)
    {
        m_map = actions != null ? actions.FindActionMap(PlayerMap) : null;
    }

    public bool IsShut { get; private set; }

    public void Shut()
    {
        if (IsShut)
        {
            return;
        }

        IsShut = true;
        if (m_map == null)
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

    public void Open()
    {
        if (!IsShut)
        {
            return;
        }

        IsShut = false;
        foreach (InputAction action in m_shut)
        {
            action.Enable();
        }

        m_shut.Clear();
    }
}
}
