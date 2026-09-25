using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Evertorch.Client
{
/// <summary>
///     Camera input (Prototype Content §3, §4): a drag with the right mouse button, the right stick, or a two-finger
///     drag orbits; the wheel, a press of the right stick, or a pinch zooms. It moves only the camera: it never walks,
///     clicks, or sends anything, and a touch that takes part in a two-finger gesture never counts as a tap.
/// </summary>
public sealed class CameraInputSource : IDisposable
{
    private const float DragDegreesPerPixel = 0.2f;
    private const float StickDegreesPerSecond = 90f;
    private const float MetresPerWheelStep = 1f;
    private const float MinimumPinchSpan = 1f;

    private readonly InputAction m_look;
    private readonly InputAction m_orbit;
    private readonly InputAction m_zoom;
    private readonly InputAction m_zoomStep;
    private readonly UiHitTest m_uiHitTest;
    private readonly HashSet<int> m_seenTouches = new();
    private readonly HashSet<int> m_worldTouches = new();
    private readonly HashSet<int> m_gestureTouches = new();
    private readonly List<TouchControl> m_pressed = new();
    private readonly List<int> m_ended = new();
    private bool m_isGesturing;
    private Vector2 m_lastMidpoint;
    private float m_lastSpan;

    public CameraInputSource(
        InputAction look,
        InputAction orbit,
        InputAction zoom,
        InputAction zoomStep,
        UiHitTest uiHitTest)
    {
        m_look = look ?? throw new ArgumentNullException(nameof(look));
        m_orbit = orbit ?? throw new ArgumentNullException(nameof(orbit));
        m_zoom = zoom ?? throw new ArgumentNullException(nameof(zoom));
        m_zoomStep = zoomStep ?? throw new ArgumentNullException(nameof(zoomStep));
        m_uiHitTest = uiHitTest ?? throw new ArgumentNullException(nameof(uiHitTest));
        m_look.Enable();
        m_orbit.Enable();
        m_zoom.Enable();
        m_zoomStep.Enable();
    }

    public void Dispose()
    {
        m_look.Disable();
        m_orbit.Disable();
        m_zoom.Disable();
        m_zoomStep.Disable();
    }

    /// <summary>
    ///     Applies this frame's camera input to <paramref name="state" />; once per frame.
    /// </summary>
    public void Apply(OrbitCameraState state, float deltaSeconds)
    {
        Vector2 stick = m_look.ReadValue<Vector2>();
        Vector2 drag = m_orbit.ReadValue<Vector2>();
        state.Orbit(
            stick.x * StickDegreesPerSecond * deltaSeconds + drag.x * DragDegreesPerPixel,
            stick.y * StickDegreesPerSecond * deltaSeconds + drag.y * DragDegreesPerPixel);

        // The input system scales the wheel to one unit a step on every platform.
        float wheel = m_zoom.ReadValue<float>();
        if (wheel != 0f)
        {
            state.Zoom(-wheel * MetresPerWheelStep);
        }

        if (m_zoomStep.WasPressedThisFrame())
        {
            state.StepZoom();
        }

        ApplyTouches(state);
    }

    /// <summary>
    ///     Whether a tap of <paramref name="touch" /> belongs to a camera gesture rather than a walk: the finger took
    ///     part in a two-finger gesture, or another finger that began off the UI is still down.
    /// </summary>
    public bool IsGestureTap(TouchControl touch)
    {
        int id = touch.touchId.ReadValue();
        if (m_gestureTouches.Contains(id))
        {
            return true;
        }

        Touchscreen? screen = Touchscreen.current;
        if (screen == null)
        {
            return false;
        }

        foreach (TouchControl other in screen.touches)
        {
            if (other.press.isPressed
                && other.touchId.ReadValue() != id
                && !m_uiHitTest.IsOverUi(other.startPosition.ReadValue()))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyTouches(OrbitCameraState state)
    {
        m_pressed.Clear();
        Touchscreen? screen = Touchscreen.current;
        if (screen != null)
        {
            foreach (TouchControl touch in screen.touches)
            {
                if (!touch.press.isPressed)
                {
                    continue;
                }

                // A finger that starts on a panel or the stick is the UI's, whatever it does next.
                int id = touch.touchId.ReadValue();
                if (m_seenTouches.Add(id) && !m_uiHitTest.IsOverUi(touch.startPosition.ReadValue()))
                {
                    m_worldTouches.Add(id);
                }

                if (m_worldTouches.Contains(id))
                {
                    m_pressed.Add(touch);
                }
            }
        }

        ForgetLiftedTouches(screen);
        if (m_pressed.Count != 2)
        {
            m_isGesturing = false;
            return;
        }

        Vector2 first = m_pressed[0].position.ReadValue();
        Vector2 second = m_pressed[1].position.ReadValue();
        Vector2 midpoint = (first + second) * 0.5f;
        float span = Vector2.Distance(first, second);
        m_gestureTouches.Add(m_pressed[0].touchId.ReadValue());
        m_gestureTouches.Add(m_pressed[1].touchId.ReadValue());
        if (m_isGesturing)
        {
            Vector2 moved = midpoint - m_lastMidpoint;
            state.Orbit(moved.x * DragDegreesPerPixel, moved.y * DragDegreesPerPixel);
            if (m_lastSpan >= MinimumPinchSpan && span >= MinimumPinchSpan)
            {
                // Fingers moving apart bring the camera in.
                state.ScaleDistance(m_lastSpan / span);
            }
        }

        m_isGesturing = true;
        m_lastMidpoint = midpoint;
        m_lastSpan = span;
    }

    // A lifted finger's tap has already been reported by the time the frame's update runs, so it can be forgotten.
    private void ForgetLiftedTouches(Touchscreen? screen)
    {
        m_ended.Clear();
        foreach (int id in m_seenTouches)
        {
            if (!IsPressed(screen, id))
            {
                m_ended.Add(id);
            }
        }

        foreach (int id in m_ended)
        {
            m_seenTouches.Remove(id);
            m_worldTouches.Remove(id);
            m_gestureTouches.Remove(id);
        }
    }

    private static bool IsPressed(Touchscreen? screen, int id)
    {
        if (screen == null)
        {
            return false;
        }

        foreach (TouchControl touch in screen.touches)
        {
            if (touch.press.isPressed && touch.touchId.ReadValue() == id)
            {
                return true;
            }
        }

        return false;
    }
}
}
