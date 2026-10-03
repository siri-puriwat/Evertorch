using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     Walking after a party member the client can see (Prototype Content §4): ordinary movement toward the member's
///     drawn position, which stops beside them and picks up again when they move away. The server learns nothing but
///     the usual movement; it ends when the member leaves view, leaves the party, or the player takes the controls.
/// </summary>
public sealed class FollowState
{
    /// <summary>
    ///     How close the follower stands to the member it follows.
    /// </summary>
    public const float FollowDistance = 2.5f;

    /// <summary>
    ///     How far past <see cref="FollowDistance" /> the member walks before the follower walks again, so a member
    ///     pacing about does not make it step back and forth.
    /// </summary>
    public const float Margin = 0.5f;

    private readonly ClientWorld m_world;
    private readonly MovementController m_controller;
    private readonly ClientParty m_party;
    private bool m_isChasing;

    public FollowState(ClientWorld world, MovementController controller, ClientParty party)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
        m_party = party ?? throw new ArgumentNullException(nameof(party));
        m_world.RemoteDespawned += OnRemoteDespawned;
    }

    public bool IsFollowing { get; private set; }

    public EntityId Target { get; private set; }

    /// <summary>
    ///     The name of the member followed, empty when nobody is.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    ///     Starts following <paramref name="member" />, a player this client has a spawn for who is in the party.
    /// </summary>
    public bool Follow(EntityId member)
    {
        if (m_world.IsLocalDead
            || !m_world.Remotes.TryGetValue(member, out RemoteEntity? remote)
            || remote.Kind != EntityKind.Player
            || !m_party.TryGetMember(remote.Name, out PartyMember? _))
        {
            return false;
        }

        End();
        Target = member;
        Name = remote.Name;
        IsFollowing = true;
        m_controller.CancelPath();
        return true;
    }

    /// <summary>
    ///     The player asked for something else: a walk of their own, an attack, a pickup, a talk, or a skill.
    /// </summary>
    public void Cancel()
    {
        if (IsFollowing)
        {
            End();
        }
    }

    /// <summary>
    ///     Runs before the movement controller on every client tick.
    /// </summary>
    public void Tick(WorldPosition position)
    {
        if (!IsFollowing)
        {
            return;
        }

        if (m_world.IsLocalDead || m_controller.HasManualDirection)
        {
            End();
            return;
        }

        if (!m_world.Remotes.TryGetValue(Target, out RemoteEntity? remote)
            || remote.IsDead
            || !m_party.TryGetMember(remote.Name, out PartyMember? _)
            || !remote.Buffer.TrySample(m_world.RemoteRenderTime, out WorldPosition target, out WorldDirection _))
        {
            End();
            return;
        }

        float distance = HorizontalDistance(position, target);
        if (distance > (m_isChasing ? FollowDistance : FollowDistance + Margin))
        {
            m_isChasing = true;
            m_controller.Chase(position, target);
            return;
        }

        if (m_isChasing && distance <= FollowDistance)
        {
            m_isChasing = false;
            m_controller.StopChase();
        }
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private void OnRemoteDespawned(RemoteEntity remote)
    {
        if (IsFollowing && remote.Entity == Target)
        {
            End();
        }
    }

    private void End()
    {
        if (m_isChasing)
        {
            m_controller.StopChase();
        }

        IsFollowing = false;
        m_isChasing = false;
        Target = default;
        Name = string.Empty;
    }
}
}
