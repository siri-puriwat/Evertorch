namespace Evertorch.Server
{
/// <summary>
///     Checkpoints every character in the world once per <c>Persistence:CheckpointIntervalMs</c>, counted from its
///     entry, so characters that entered at different times do not all write on the same tick (Persistence §6).
/// </summary>
public sealed class CheckpointScheduler : ITickPhase
{
    private readonly SessionRegistry m_sessions;
    private readonly CharacterLifetime m_lifetime;

    public CheckpointScheduler(SessionRegistry sessions, CharacterLifetime lifetime)
    {
        m_sessions = sessions;
        m_lifetime = lifetime;
    }

    public TickPhase Phase => TickPhase.SchedulePersistence;

    public void Execute(in TickContext context)
    {
        foreach (CharacterSession character in m_sessions.Characters)
        {
            if (unchecked((int)(context.Tick - character.NextCheckpointTick)) >= 0 && !character.IsLoggingOut)
            {
                character.NextCheckpointTick = context.Tick + m_lifetime.CheckpointIntervalTicks;
                m_lifetime.QueueCheckpoint(character);
            }
        }
    }
}
}
