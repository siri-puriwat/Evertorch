using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     The skill bar's keys and gamepad buttons (Prototype Content §4). It only reports which slot was asked for; the
///     slot's button on the bar asks for the same thing through <see cref="GameClient.UseSkillSlot" />.
/// </summary>
public sealed class SkillInputSource : IDisposable
{
    private readonly InputAction[] m_slots;
    private readonly Action<InputAction.CallbackContext>[] m_handlers;
    private int m_requested;

    /// <param name="slots">The actions of slots 1, 2, and onwards, in that order.</param>
    public SkillInputSource(IReadOnlyList<InputAction> slots)
    {
        if (slots == null)
        {
            throw new ArgumentNullException(nameof(slots));
        }

        m_slots = new InputAction[slots.Count];
        m_handlers = new Action<InputAction.CallbackContext>[slots.Count];
        for (int index = 0; index < slots.Count; index++)
        {
            int slot = index + 1;
            m_slots[index] = slots[index] ?? throw new ArgumentException("A slot has no action.", nameof(slots));
            m_handlers[index] = _ => m_requested = slot;
            m_slots[index].performed += m_handlers[index];
            m_slots[index].Enable();
        }
    }

    public void Dispose()
    {
        for (int index = 0; index < m_slots.Length; index++)
        {
            m_slots[index].performed -= m_handlers[index];
        }
    }

    /// <summary>
    ///     The slot asked for since the last call, numbered from 1; 0 for none.
    /// </summary>
    public int TakeSlot()
    {
        int slot = m_requested;
        m_requested = 0;
        return slot;
    }
}
}
