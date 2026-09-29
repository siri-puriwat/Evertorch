using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The local side of a skill (Gameplay Systems §5.1): a skill on the caster is asked for at once, and one on an
///     enemy or a player walks within the skill's range, as an attack does, before it is asked for. Its target is the
///     selection (<see cref="Use" />, the gamepad's way) or one a click or tap chose after the press
///     (<see cref="UseAt" />). The server checks the skill, its cost, its range, and its target again and decides;
///     nothing here predicts the cast.
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

    /// <summary>
    ///     How long a sent request is waited on at most: past any round trip the game plays over, for a word lost with
    ///     its connection.
    /// </summary>
    private const double CastAwaitSeconds = 2.0;

    private readonly ClientWorld m_world;
    private readonly MovementController m_controller;

    private readonly ISkillCommandSink m_commands;
    private readonly int m_nextSwingMarginTicks;
    private readonly int m_castAwaitTicks;
    private float m_range;
    private uint m_sentSequence;
    private bool m_isAimed;

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
        m_castAwaitTicks = (int)Math.Ceiling(CastAwaitSeconds / tickSeconds);
        m_world.TargetChanged += OnTargetChanged;
        m_world.CommandRejectedReceived += OnCommandRejected;
    }

    public bool IsActive { get; private set; }

    public SkillDefinitionId Skill { get; private set; }

    /// <summary>
    ///     The enemy or the player the skill is for; the default value for a skill on the caster.
    /// </summary>
    public EntityId Target { get; private set; }

    public int SkillsSent { get; private set; }

    /// <summary>
    ///     Asks for <paramref name="skill" /> on the caster, at the confirmed target for an enemy skill, or for an ally
    ///     skill at the selected player, else on the caster (Gameplay Systems §9). False, with nothing started, while
    ///     dead, for a skill the server has not listed, and for an enemy skill without a target or at a player.
    /// </summary>
    public bool Use(SkillDefinitionId skill, SkillTargetType targetType)
    {
        EntityId target = targetType switch
        {
            SkillTargetType.Enemy => m_world.Target,
            SkillTargetType.Ally when m_world.IsPlayer(m_world.Target) => m_world.Target,
            _ => default
        };
        if (m_world.IsLocalDead
            || !TryGetRange(skill, out float range)
            || (targetType == SkillTargetType.Enemy && (target == default || m_world.IsPlayer(target))))
        {
            return false;
        }

        Start(skill, target, range, false);
        return true;
    }

    /// <summary>
    ///     Asks for <paramref name="skill" /> at <paramref name="target" />, which a click or tap chose after the press
    ///     (Prototype Content §4): a live monster for an enemy skill, a live player for an ally skill, or the caster, by its
    ///     own ID or the default value, for an ally skill. The selection plays no part, so a later change of it leaves
    ///     the skill alone. False, with nothing started, while dead, for a skill the server has not listed, and for a
    ///     target the skill cannot take.
    /// </summary>
    public bool UseAt(SkillDefinitionId skill, SkillTargetType targetType, EntityId target)
    {
        if (target == m_world.LocalEntity)
        {
            target = default;
        }

        bool isTakeable = target == default
            ? targetType != SkillTargetType.Enemy
            : targetType != SkillTargetType.Self && SkillTargeting.CanTake(m_world, targetType, target);
        if (m_world.IsLocalDead || !TryGetRange(skill, out float range) || !isTakeable)
        {
            return false;
        }

        Start(skill, target, range, true);
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
    ///     The player cleared what it was after (Esc, the gamepad's East, the Clear button): a skill still walking to its
    ///     target ends, and one on the caster goes on.
    /// </summary>
    public void CancelApproach()
    {
        if (IsActive && Target != default)
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
        if (!actionLock.HasBeenFreeFor(ActionLock.ClearTicks) || actionLock.IsSwingDueWithin(m_nextSwingMarginTicks))
        {
            return;
        }

        // It refuses one during the after-cast delay or the skill's cooldown too, so the press is held through them.
        if (m_world.AfterCastDelayRemaining > 0.0 || m_world.CooldownRemaining(Skill) > 0.0)
        {
            return;
        }

        // The server may begin the cast before the client hears of it, and a cancel meanwhile would end it there.
        m_sentSequence = m_commands.SendUseSkill(Skill, Target);
        actionLock.AwaitCast(m_castAwaitTicks);
        SkillsSent++;
        IsActive = false;
        Skill = default;
        Target = default;
        m_isAimed = false;
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

    // A refused request begins no cast.
    private void OnCommandRejected(CommandRejected rejected)
    {
        if (m_sentSequence != 0 && rejected.CommandSequence == m_sentSequence)
        {
            m_sentSequence = 0;
            m_world.ActionLock.StopAwaitingCast();
        }
    }

    // The player cleared the target or chose another: an approach to the selection was for the one before. A skill aimed
    // by a click or tap is not the selection's, so a change of it leaves the skill alone.
    private void OnTargetChanged()
    {
        if (IsActive && !m_isAimed && Target != default && m_world.Target != Target)
        {
            End();
        }
    }

    private void Start(SkillDefinitionId skill, EntityId target, float range, bool isAimed)
    {
        End();
        Skill = skill;
        Target = target;
        m_range = range;
        m_isAimed = isAimed;
        IsActive = true;

        // An approach replaces a walk the player asked for, as an attack's does.
        if (target != default && !m_controller.IsChasing)
        {
            m_controller.CancelPath();
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
        m_isAimed = false;
    }
}
}
