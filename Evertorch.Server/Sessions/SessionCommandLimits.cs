namespace Evertorch.Server
{
/// <summary>
///     Layer 2 of the abuse controls: one bucket per class of command, per connection, so a reconnect starts with
///     full buckets (Network Protocol §3, §11). <c>CancelAction</c> and movement belong to no class: a cancel is
///     idempotent and the client never waits for its answer, and movement is limited to one input per tick.
/// </summary>
public sealed class SessionCommandLimits
{
    private readonly TickBucket m_combat;
    private readonly TickBucket m_pickup;
    private readonly TickBucket m_session;
    private readonly TickBucket m_resync;

    public SessionCommandLimits(AbuseOptions options, int tickRate, uint tick)
    {
        m_combat = new TickBucket(options.CombatCommandsPerSecond, options.CombatCommandBurst, tickRate, tick);
        m_pickup = new TickBucket(options.PickupCommandsPerSecond, options.PickupCommandBurst, tickRate, tick);
        m_session = new TickBucket(options.SessionCommandsPerSecond, options.SessionCommandBurst, tickRate, tick);
        m_resync = new TickBucket(options.ResyncRequestsPerSecond, options.ResyncRequestBurst, tickRate, tick);
    }

    /// <summary>
    ///     False when the command's class has no token left; <paramref name="limit" /> then names the class.
    /// </summary>
    public bool TryTake(InboundEventKind kind, uint tick, out string limit)
    {
        switch (kind)
        {
            case InboundEventKind.Attack:
            case InboundEventKind.UseSkill:
            case InboundEventKind.Target:
            case InboundEventKind.Respawn:
                limit = ServerInstruments.CombatCommandLimit;
                return m_combat.TryTake(tick);
            case InboundEventKind.Pickup:
                limit = ServerInstruments.PickupCommandLimit;
                return m_pickup.TryTake(tick);
            case InboundEventKind.CreateCharacter:
            case InboundEventKind.EnterWorld:
            case InboundEventKind.Logout:
                limit = ServerInstruments.SessionCommandLimit;
                return m_session.TryTake(tick);
            case InboundEventKind.InventoryResync:
                limit = ServerInstruments.ResyncRequestLimit;
                return m_resync.TryTake(tick);
            default:
                limit = string.Empty;
                return true;
        }
    }
}
}
