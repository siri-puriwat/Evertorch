namespace Evertorch.Server
{
/// <summary>
///     Moves the characters that stand in a portal after the last tick's movement to the portal's destination
///     (Gameplay Systems §4.2), at the start of the tick and inside each session's fault boundary.
/// </summary>
public sealed class MapTransferPhase : ITickPhase
{
    private readonly SessionManager m_sessions;

    public MapTransferPhase(SessionManager sessions)
    {
        m_sessions = sessions;
    }

    public TickPhase Phase => TickPhase.ApplyCommands;

    public void Execute(in TickContext context)
    {
        m_sessions.TransferThroughPortals(context.Tick);
    }
}
}
