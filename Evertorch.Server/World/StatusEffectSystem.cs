using Evertorch.Game;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Status effects on players (Gameplay Systems §9.1): a skill starts one or renews its duration, and it ends at its
///     time, on death, and with the character's time in the world. Every start and end recalculates the statistics,
///     and every change tells the owner. Registered before <see cref="CombatSystem" />, so a swing that begins in the
///     tick an effect ends already has the statistics without it.
/// </summary>
public sealed class StatusEffectSystem : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly WorldSimulation m_world;
    private readonly SessionRegistry m_sessions;
    private readonly ServerContent m_content;
    private readonly CharacterStats m_stats;
    private readonly MessageSender m_sender;
    private readonly ServerInstruments m_instruments;
    private readonly int m_tickRate;

    public StatusEffectSystem(
        WorldSimulation world,
        SessionRegistry sessions,
        ServerContent content,
        CharacterStats stats,
        MessageSender sender,
        ServerInstruments instruments,
        IOptions<SimulationOptions> simulation)
    {
        m_world = world;
        m_sessions = sessions;
        m_content = content;
        m_stats = stats;
        m_sender = sender;
        m_instruments = instruments;
        m_tickRate = simulation.Value.TickRate;
    }

    public TickPhase Phase => TickPhase.Combat;

    public void Execute(in TickContext context)
    {
        long now = (long)(context.Tick - 1) * MillisecondsPerSecond / m_tickRate;
        foreach (MapInstance map in m_world.Maps)
        {
            foreach (WorldEntity entity in map.Entities)
            {
                if (entity is PlayerEntity player && player.StatusEffects.Count > 0)
                {
                    int ended = player.EndStatusEffectsDueBy(now);
                    if (ended > 0)
                    {
                        m_instruments.RecordStatusEffects(StatusEffectChange.Ended, ended);
                        Recalculate(player);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Starts <paramref name="status" /> on <paramref name="player" /> until <paramref name="endMs" />, or renews it
    ///     to end then when it is already active.
    /// </summary>
    public void Apply(PlayerEntity player, StatusDefinitionId status, long endMs)
    {
        StatusEffectDefinition definition = m_content.StatusEffects[status];
        if (player.StartStatusEffect(new ActiveStatusEffect(status, definition.StatPercent, endMs)))
        {
            m_instruments.RecordStatusEffects(StatusEffectChange.Started, 1);
            Recalculate(player);
        }
        else
        {
            m_instruments.RecordStatusEffects(StatusEffectChange.Renewed, 1);
            TellOwner(player);
        }
    }

    /// <summary>
    ///     Death ends every effect (Gameplay Systems §9.1).
    /// </summary>
    public void EndAll(PlayerEntity player)
    {
        int ended = player.EndAllStatusEffects();
        if (ended > 0)
        {
            m_instruments.RecordStatusEffects(StatusEffectChange.Ended, ended);
            Recalculate(player);
        }
    }

    // The maximums can change with the statistics; the owner hears of new ones as of any other change of its HP or SP.
    private void Recalculate(PlayerEntity player)
    {
        int maxHealth = player.MaxHealth;
        int maxSpirit = player.MaxSpirit;
        m_stats.Recalculate(player, m_content.Jobs[player.Job]);
        if (player.MaxHealth != maxHealth || player.MaxSpirit != maxSpirit)
        {
            m_sender.SendHealth(player);
        }

        TellOwner(player);
    }

    private void TellOwner(PlayerEntity player)
    {
        if (m_sessions.TryGet(player.Owner, out ClientSession? session) && session != null && session.Player == player)
        {
            session.NeedsStatusEffects = true;
        }
    }
}
}
