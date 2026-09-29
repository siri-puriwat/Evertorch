using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Evertorch.Client
{
/// <summary>
///     The skill bar's keys and gamepad buttons (Prototype Content §4). It only reports which slot was asked for; the
///     slot's button on the bar asks for the same thing through <see cref="GameClient.UseSkillSlot" />. The gamepad has
///     three skill buttons, South and the triggers, so D-pad left pages them between slots 1 to 3 and slots 6 to 8.
/// </summary>
public sealed class SkillInputSource : IDisposable
{
    private const int GamepadSkillButtons = 3;

    private readonly InputAction[] m_slots;
    private readonly Action<InputAction.CallbackContext>[] m_handlers;
    private readonly InputAction? m_page;
    private readonly Action<InputAction.CallbackContext> m_pageHandler;
    private int m_requested;
    private bool m_isRequestedByGamepad;
    private bool m_isPageToggled;

    /// <param name="slots">The actions of slots 1, 2, and onwards, in that order.</param>
    /// <param name="page">The action that pages the gamepad's skill buttons, when there is one.</param>
    public SkillInputSource(IReadOnlyList<InputAction> slots, InputAction? page = null)
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
            m_handlers[index] = context => Request(slot, context.control?.device);
            m_slots[index].performed += m_handlers[index];
            m_slots[index].Enable();
        }

        m_page = page;
        m_pageHandler = _ => m_isPageToggled = true;
        if (m_page != null)
        {
            m_page.performed += m_pageHandler;
            m_page.Enable();
        }
    }

    /// <summary>
    ///     Whether the gamepad's skill buttons ask for slots 6 to 8 rather than 1 to 3.
    /// </summary>
    public bool IsOnOwnSkills { get; set; }

    public void Dispose()
    {
        for (int index = 0; index < m_slots.Length; index++)
        {
            m_slots[index].performed -= m_handlers[index];
        }

        if (m_page != null)
        {
            m_page.performed -= m_pageHandler;
        }
    }

    /// <summary>
    ///     Out of the world: forgets the page and a turn asked for, so the next entry starts on slots 1 to 3.
    /// </summary>
    public void Reset()
    {
        m_isPageToggled = false;
        IsOnOwnSkills = false;
    }

    /// <summary>
    ///     The slot asked for since the last call, numbered from 1; 0 for none.
    /// </summary>
    public int TakeSlot()
    {
        return TakeSlot(out bool _);
    }

    /// <summary>
    ///     The slot asked for since the last call, numbered from 1, or 0 for none; <paramref name="isFromGamepad" /> says
    ///     whether a gamepad button asked, since a gamepad has no pointer to choose a skill's target with (Prototype
    ///     Content §4).
    /// </summary>
    public int TakeSlot(out bool isFromGamepad)
    {
        int slot = m_requested;
        isFromGamepad = slot != 0 && m_isRequestedByGamepad;
        m_requested = 0;
        m_isRequestedByGamepad = false;
        return slot;
    }

    /// <summary>
    ///     Whether the page was asked to turn since the last call.
    /// </summary>
    public bool TakePageToggle()
    {
        bool isToggled = m_isPageToggled;
        m_isPageToggled = false;
        return isToggled;
    }

    private void Request(int slot, InputDevice? device)
    {
        m_requested = SlotFor(slot, device);
        m_isRequestedByGamepad = device is Gamepad;
    }

    // Only the gamepad's buttons follow the page; the keys 1 to 8 always ask for their own slot.
    private int SlotFor(int slot, InputDevice? device)
    {
        return IsOnOwnSkills && device is Gamepad && slot <= GamepadSkillButtons
            ? slot + SkillSlots.FirstOwnSlot - 1
            : slot;
    }
}
}
