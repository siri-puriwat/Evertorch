using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The local side of an auto-attack (Gameplay Systems §5.1, §6): it asks the server to attack, walks the
///     character into range with ordinary movement, holds movement while its own swing is committed, and asks the
///     server to stop when the player moves on. It never decides a hit, a swing, or its timing.
/// </summary>
public sealed class AutoAttackState
{
    /// <summary>
    ///     How far inside the attack range the approach stops, so small differences in the target's drawn position
    ///     still leave it in range when the server checks.
    /// </summary>
    public const float StopMargin = 0.2f;

    private const float MinimumStopDistance = 0.1f;
    private const double StalledSeconds = 1.0;

    private readonly ClientWorld m_world;
    private readonly MovementController m_controller;
    private readonly ICombatCommandSink m_commands;
    private readonly double m_tickSeconds;
    private readonly int m_stalledTicks;
    private int m_ticksInRangeWithoutSwing;
    private bool m_isClosingIn;
    private bool m_isAwaitingConfirmation;
    private uint m_attackSequence;

    public AutoAttackState(
        ClientWorld world,
        MovementController controller,
        ICombatCommandSink commands,
        double tickSeconds)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
        m_commands = commands ?? throw new ArgumentNullException(nameof(commands));
        if (!(tickSeconds > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(tickSeconds));
        }

        m_tickSeconds = tickSeconds;
        m_stalledTicks = (int)Math.Ceiling(StalledSeconds / tickSeconds);
        m_world.AttackStartedReceived += OnAttackStarted;
        m_world.TargetChanged += OnTargetChanged;
        m_world.CommandRejectedReceived += OnCommandRejected;
    }

    public bool IsActive { get; private set; }

    public EntityId Target { get; private set; }

    public int CancelsSent { get; private set; }

    /// <summary>
    ///     Asks the server to attack <paramref name="target" /> and starts walking up to it.
    /// </summary>
    public void Attack(EntityId target)
    {
        if (target == default || m_world.IsLocalDead)
        {
            return;
        }

        m_attackSequence = m_commands.SendAttack(target);

        // The server answers only a change of target, so only then is there a reply to wait for.
        m_isAwaitingConfirmation = target != m_world.Target;
        Target = target;
        IsActive = true;
        m_isClosingIn = false;
        m_ticksInRangeWithoutSwing = 0;

        // An attack replaces a walk the player asked for: a walk in progress would keep every swing from starting.
        if (!m_controller.IsChasing)
        {
            m_controller.CancelPath();
        }
    }

    /// <summary>
    ///     The player asked for a walk of their own; approach steps never come through here.
    /// </summary>
    public void OnWalkRequested()
    {
        if (IsActive)
        {
            SendCancel();
        }
    }

    /// <summary>
    ///     Runs before the movement controller on every client tick, after the tick's lock was applied.
    /// </summary>
    public void Tick(WorldPosition position)
    {
        if (!IsActive)
        {
            return;
        }

        if (m_world.IsLocalDead)
        {
            End();
            return;
        }

        if (m_controller.HasManualDirection)
        {
            SendCancel();
            return;
        }

        if (!m_world.Remotes.TryGetValue(Target, out RemoteEntity? remote)
            || remote.IsDead
            || !remote.Buffer.TrySample(m_world.RemoteRenderTime, out WorldPosition target, out WorldDirection _))
        {
            End();
            return;
        }

        float stopDistance = m_isClosingIn
            ? m_world.AttackRange * 0.5f
            : Math.Max(MinimumStopDistance, m_world.AttackRange - StopMargin);
        if (HorizontalDistance(position, target) > stopDistance)
        {
            m_controller.Chase(position, target);
            m_ticksInRangeWithoutSwing = 0;
            return;
        }

        m_controller.StopChase();
        // No swing begins while the player swings or casts (Gameplay Systems §6), so only a tick the swing's or the
        // cast's hold does not cover counts toward a stall. The driver has already set this tick's hold.
        if (m_controller.IsLocked)
        {
            m_ticksInRangeWithoutSwing = 0;
        }
        else if (++m_ticksInRangeWithoutSwing > m_stalledTicks)
        {
            // The server measures range against the target's own position, which the drawn one trails; come closer.
            m_isClosingIn = true;
        }
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private void OnAttackStarted(AttackStarted started)
    {
        if (started.Attacker != m_world.LocalEntity)
        {
            return;
        }

        // The lock is counted from hearing of the swing: the server locked the same span a little earlier.
        m_world.ActionLock.LockForSwing(
            (int)Math.Ceiling(started.Timing.Impact.TotalSeconds / m_tickSeconds),
            (int)Math.Ceiling(started.Timing.Interval.TotalSeconds / m_tickSeconds));
        m_ticksInRangeWithoutSwing = 0;
        m_isClosingIn = false;
    }

    // The server ends an auto-attack by clearing or replacing the target: the target died, left view, or the
    // player picked another. Until the server confirms the target asked for, a change is older news that crossed
    // the request, such as the confirmation of the previous target or its death.
    private void OnTargetChanged()
    {
        if (!IsActive)
        {
            return;
        }

        if (m_isAwaitingConfirmation)
        {
            m_isAwaitingConfirmation = m_world.Target != Target;
            return;
        }

        if (m_world.Target != Target)
        {
            End();
        }
    }

    private void SendCancel()
    {
        m_commands.SendCancel();
        CancelsSent++;
        End();
    }

    // The server refused the attack this chase is for (Network Protocol §11), so there is nothing to walk up to. A
    // refusal of an older attack says nothing about the current one.
    private void OnCommandRejected(CommandRejected rejected)
    {
        if (IsActive && m_attackSequence != 0 && rejected.CommandSequence == m_attackSequence)
        {
            End();
        }
    }

    private void End()
    {
        IsActive = false;
        m_isAwaitingConfirmation = false;
        Target = default;
        m_controller.StopChase();
    }
}
}
