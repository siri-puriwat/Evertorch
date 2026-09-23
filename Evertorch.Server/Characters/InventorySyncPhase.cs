using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Sends a whole inventory to each client that needs one: after entering or attaching, and when a client asks for
///     a resynchronization. Registered after <see cref="VisibilityPhase" /> in the same phase, so on entering the parts
///     follow the baseline's spawns (Network Protocol §9).
/// </summary>
public sealed class InventorySyncPhase : ITickPhase
{
    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;

    public InventorySyncPhase(SessionRegistry sessions, MessageSender sender)
    {
        m_sessions = sessions;
        m_sender = sender;
    }

    public TickPhase Phase => TickPhase.FinalizeWorld;

    public void Execute(in TickContext context)
    {
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (!session.NeedsInventorySnapshot || session.State != SessionState.InWorld || session.Character == null)
            {
                continue;
            }

            session.NeedsInventorySnapshot = false;
            foreach (InventorySnapshot part in session.Character.Inventory.CreateSnapshot())
            {
                m_sender.Send(session.Connection, part);
            }
        }
    }
}
}
