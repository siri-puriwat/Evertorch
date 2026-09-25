using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Sends each owner the state of its own character that no other message carries: the skill list, with what is
///     left of each cooldown, after the inventory in every baseline and whenever one of its casts resolves (Network
///     Protocol §9). Registered after <see cref="InventorySyncPhase" /> in the same phase.
/// </summary>
public sealed class CharacterSyncPhase : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly SessionRegistry m_sessions;
    private readonly ServerContent m_content;
    private readonly MessageSender m_sender;
    private readonly int m_tickRate;
    private readonly List<SkillListEntry> m_entries = new();

    public CharacterSyncPhase(
        SessionRegistry sessions,
        ServerContent content,
        MessageSender sender,
        IOptions<SimulationOptions> simulation)
    {
        m_sessions = sessions;
        m_content = content;
        m_sender = sender;
        m_tickRate = simulation.Value.TickRate;
    }

    public TickPhase Phase => TickPhase.FinalizeWorld;

    public void Execute(in TickContext context)
    {
        long now = (long)(context.Tick - 1) * MillisecondsPerSecond / m_tickRate;
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (!session.NeedsSkillList || session.State != SessionState.InWorld || session.Player == null)
            {
                continue;
            }

            session.NeedsSkillList = false;
            m_sender.Send(session.Connection, CreateSkillList(session.Player, now));
        }
    }

    private SkillList CreateSkillList(PlayerEntity player, long now)
    {
        m_entries.Clear();
        foreach (SkillDefinitionId id in m_content.Jobs[player.Job].Skills)
        {
            SkillDefinition skill = m_content.Skills[id];
            long end = player.Combat.CooldownEndMs(id);
            long remaining = end == long.MinValue ? 0 : Math.Max(0, end - now);
            m_entries.Add(
                new SkillListEntry(
                    id,
                    (float)skill.Range,
                    (uint)skill.SpCost,
                    (uint)skill.CooldownMs,
                    (uint)skill.AfterCastDelayMs,
                    (uint)Math.Min(remaining, skill.CooldownMs)));
        }

        return new SkillList(m_entries.ToArray());
    }
}
}
