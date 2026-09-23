using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The local side of a pickup (Gameplay Systems §11): it walks the character up to the drop with ordinary movement
///     and then asks the server once. The server checks range again and commits; the drop is gone only when the
///     server says so.
/// </summary>
public sealed class PickupState
{
    /// <summary>
    ///     How close the approach comes before asking: well inside the server's reach of 1.5 m plus its tolerance, so
    ///     the drawn position of the character trailing the predicted one does not matter.
    /// </summary>
    public const float StopDistance = 1.0f;

    /// <summary>
    ///     How far the pickup key looks for the nearest drop.
    /// </summary>
    public const float KeyReach = 5f;

    private readonly ClientWorld m_world;
    private readonly MovementController m_controller;
    private readonly IPickupCommandSink m_commands;
    private uint m_sequence;

    public PickupState(ClientWorld world, MovementController controller, IPickupCommandSink commands)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
        m_commands = commands ?? throw new ArgumentNullException(nameof(commands));
        m_world.CommandRejectedReceived += OnCommandRejected;
        m_world.RemoteDespawned += OnRemoteDespawned;
    }

    public bool IsActive { get; private set; }

    public EntityId Drop { get; private set; }

    /// <summary>
    ///     The request was sent and its answer is awaited.
    /// </summary>
    public bool IsSent => m_sequence != 0;

    public int PickupsSent { get; private set; }

    /// <summary>
    ///     Starts walking up to <paramref name="drop" />, a drop this client has a spawn for.
    /// </summary>
    public void Pickup(EntityId drop)
    {
        if (m_world.IsLocalDead
            || !m_world.Remotes.TryGetValue(drop, out RemoteEntity? remote)
            || remote.Kind != EntityKind.ItemDrop)
        {
            return;
        }

        End();
        Drop = drop;
        IsActive = true;
        m_controller.CancelPath();
    }

    /// <summary>
    ///     The player asked for something else: a walk of their own or an attack.
    /// </summary>
    public void Cancel()
    {
        if (IsActive)
        {
            End();
        }
    }

    /// <summary>
    ///     Runs before the movement controller on every client tick.
    /// </summary>
    public void Tick(WorldPosition position)
    {
        if (!IsActive || IsSent)
        {
            return;
        }

        if (m_world.IsLocalDead || m_controller.HasManualDirection)
        {
            End();
            return;
        }

        if (!m_world.Remotes.TryGetValue(Drop, out RemoteEntity? remote)
            || !remote.Buffer.TrySample(m_world.RemoteRenderTime, out WorldPosition target, out WorldDirection _))
        {
            End();
            return;
        }

        if (HorizontalDistance(position, target) > StopDistance)
        {
            m_controller.Chase(position, target);
            return;
        }

        m_controller.StopChase();
        m_sequence = m_commands.SendPickup(Drop);
        PickupsSent++;
        if (m_sequence == 0)
        {
            End();
        }
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private void OnCommandRejected(CommandRejected rejected)
    {
        if (IsActive && IsSent && rejected.CommandSequence == m_sequence)
        {
            End();
        }
    }

    // Picked up by anyone, expired, or out of view: there is nothing left to walk to or wait for.
    private void OnRemoteDespawned(RemoteEntity remote)
    {
        if (IsActive && remote.Entity == Drop)
        {
            End();
        }
    }

    private void End()
    {
        if (IsActive && !IsSent)
        {
            m_controller.StopChase();
        }

        IsActive = false;
        Drop = default;
        m_sequence = 0;
    }
}
}
