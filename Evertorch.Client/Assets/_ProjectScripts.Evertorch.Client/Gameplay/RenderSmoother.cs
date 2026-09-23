using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Decides where the local player is drawn. It blends between the last two simulation ticks, and when the server
///     corrects the prediction it hides a small error by letting it fade out, or jumps when the error is too large to
///     pass off as movement.
/// </summary>
public sealed class RenderSmoother
{
    public const float TeleportThreshold = 2f;
    public const float CorrectionHalfLifeSeconds = 0.06f;

    private const float NegligibleOffset = 0.0005f;

    private WorldPosition m_previous;
    private WorldPosition m_current;
    private float m_offsetX;
    private float m_offsetY;
    private float m_offsetZ;

    public RenderSmoother(WorldPosition position)
    {
        m_previous = position;
        m_current = position;
    }

    public int Snaps { get; private set; }

    public float LastCorrection { get; private set; }

    public float LargestCorrection { get; private set; }

    public float PendingOffset =>
        (float)Math.Sqrt(m_offsetX * m_offsetX + m_offsetY * m_offsetY + m_offsetZ * m_offsetZ);

    public void OnTick(WorldPosition previous, WorldPosition current)
    {
        m_previous = previous;
        m_current = current;
    }

    /// <summary>
    ///     Call with the predicted position just before and just after a reconciliation.
    /// </summary>
    public void OnCorrected(WorldPosition before, WorldPosition after)
    {
        float shiftX = after.X - before.X;
        float shiftY = after.Y - before.Y;
        float shiftZ = after.Z - before.Z;
        float error = (float)Math.Sqrt(shiftX * shiftX + shiftY * shiftY + shiftZ * shiftZ);
        LastCorrection = error;
        LargestCorrection = Math.Max(LargestCorrection, error);

        if (error > TeleportThreshold)
        {
            Snaps++;
            m_offsetX = 0f;
            m_offsetY = 0f;
            m_offsetZ = 0f;
            m_previous = after;
            m_current = after;
            return;
        }

        // Both tick positions move with the correction and the offset cancels it, so the drawn point stays put and
        // then drifts to the corrected one.
        m_previous = new WorldPosition(m_previous.X + shiftX, m_previous.Y + shiftY, m_previous.Z + shiftZ);
        m_current = after;
        m_offsetX -= shiftX;
        m_offsetY -= shiftY;
        m_offsetZ -= shiftZ;
    }

    /// <summary>
    ///     Draws the player at a new place at once. Unlike a large correction, a move the server announced is not
    ///     counted as a snap.
    /// </summary>
    public void Teleport(WorldPosition position)
    {
        m_offsetX = 0f;
        m_offsetY = 0f;
        m_offsetZ = 0f;
        m_previous = position;
        m_current = position;
    }

    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        float keep = (float)Math.Pow(0.5, deltaSeconds / CorrectionHalfLifeSeconds);
        m_offsetX *= keep;
        m_offsetY *= keep;
        m_offsetZ *= keep;
        if (PendingOffset < NegligibleOffset)
        {
            m_offsetX = 0f;
            m_offsetY = 0f;
            m_offsetZ = 0f;
        }
    }

    /// <param name="alpha">How far the frame is between the previous tick (0) and the current one (1).</param>
    public WorldPosition Sample(float alpha)
    {
        float t = Math.Max(0f, Math.Min(1f, alpha));
        return new WorldPosition(
            m_previous.X + (m_current.X - m_previous.X) * t + m_offsetX,
            m_previous.Y + (m_current.Y - m_previous.Y) * t + m_offsetY,
            m_previous.Z + (m_current.Z - m_previous.Z) * t + m_offsetZ);
    }
}
}
