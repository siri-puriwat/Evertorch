using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The local side of a skill (Gameplay Systems §5.1): a skill on the caster is asked for at once, and one on an
///     enemy walks within the skill's range, as an attack does, before it is asked for. The server checks the skill,
///     its cost, its range, and its target again and decides; nothing here predicts the cast.
/// </summary>
public sealed class SkillState
{
    private const float MinimumStopDistance = 0.1f;

    /// <summary>
    ///     How long before the auto-attack's next swing a request is held back. The client hears of each swing a little
    ///     after the server began it, and the request needs a little longer again to arrive, so a request sent just
    ///     before the swing would find it under way.
    /// </summary>
    private const double NextSwingMarginSeconds = 0.2;

    private const int ClearTicks = 2;

    private readonly ClientWorld m_world;
    private readonly MovementController m_controller;
    private readonly ISkillCommandSink m_commands;
    private readonly int m_nextSwingMarginTicks;
    private float m_range;

    public SkillState(ClientWorld world, MovementController controller, ISkillCommandSink commands, double tickSeconds)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
        m_commands = commands ?? throw new ArgumentNullException(nameof(commands));
        if (!(tickSeconds > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(tickSeconds));
        }

        m_nextSwingMarginTicks = (int)Math.Ceiling(NextSwingMarginSeconds / tickSeconds);
        m_world.TargetChanged += OnTargetChanged;
    }

    public bool IsActive { get; private set; }

    public SkillDefinitionId Skill { get; private set; }

    /// <summary>
    ///     The enemy the skill is for; the default value for a skill on the caster.
    /// </summary>
    public EntityId Target { get; private set; }

    public int SkillsSent { get; private set; }

    /// <summary>
    ///     Asks for <paramref name="skill" /> on the caster, or at the confirmed target for an enemy skill. False, with
    ///     nothing started, while dead, for a skill the server has not listed, and for an enemy skill without a target.
    /// </summary>
    public bool Use(SkillDefinitionId skill, SkillTargetType targetType)
    {
        EntityId target = targetType == SkillTargetType.Enemy ? m_world.Target : default;
        if (m_world.IsLocalDead
            || !TryGetRange(skill, out float range)
            || (targetType == SkillTargetType.Enemy && target == default))
        {
            return false;
        }

        End();
        Skill = skill;
        Target = target;
        m_range = range;
        IsActive = true;

        // An approach replaces a walk the player asked for, as an attack's does.
        if (target != default && !m_controller.IsChasing)
        {
            m_controller.CancelPath();
        }

        return true;
    }

    /// <summary>
    ///     The player asked for something else: a walk of their own, an attack, or a pickup.
    /// </summary>
    public void Cancel()
    {
        if (IsActive)
        {
            End();
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

        if (m_world.IsLocalDead || m_controller.HasManualDirection)
        {
            End();
            return;
        }

        if (Target != default)
        {
            if (!m_world.Remotes.TryGetValue(Target, out RemoteEntity? remote)
                || remote.IsDead
                || !remote.Buffer.TrySample(m_world.RemoteRenderTime, out WorldPosition target, out WorldDirection _))
            {
                End();
                return;
            }

            float stopDistance = Math.Max(MinimumStopDistance, m_range - AutoAttackState.StopMargin);
            if (HorizontalDistance(position, target) > stopDistance)
            {
                m_controller.Chase(position, target);
                return;
            }

            m_controller.StopChase();
        }

        // The server refuses a cast while the player's own swing or cast is under way, so the request waits until
        // both are surely over there, and for the impact of a swing that would begin before the request arrived.
        ActionLock actionLock = m_world.ActionLock;
        if (!actionLock.HasBeenFreeFor(ClearTicks) || actionLock.IsSwingDueWithin(m_nextSwingMarginTicks))
        {
            return;
        }

        m_commands.SendUseSkill(Skill, Target);
        SkillsSent++;
        IsActive = false;
        Skill = default;
        Target = default;
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private bool TryGetRange(SkillDefinitionId skill, out float range)
    {
        foreach (SkillListEntry entry in m_world.Skills)
        {
            if (entry.Skill == skill)
            {
                range = entry.Range;
                return true;
            }
        }

        range = 0f;
        return false;
    }

    // The player cleared the target or chose another: the approach was for the one before.
    private void OnTargetChanged()
    {
        if (IsActive && Target != default && m_world.Target != Target)
        {
            End();
        }
    }

    private void End()
    {
        if (IsActive && Target != default)
        {
            m_controller.StopChase();
        }

        IsActive = false;
        Skill = default;
        Target = default;
    }
}
}
