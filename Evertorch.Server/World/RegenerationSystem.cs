using System;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Natural regeneration (Gameplay Systems §2.1): a live player regains HP and SP on the first tick at or after
///     each step of its own schedule, counted from the tick it was placed, walking or not. A step at full HP or SP
///     changes and sends nothing, and the schedule goes on through death.
/// </summary>
public sealed class RegenerationSystem : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly WorldSimulation m_world;
    private readonly MessageSender m_sender;
    private readonly int m_tickRate;

    public RegenerationSystem(WorldSimulation world, MessageSender sender, IOptions<SimulationOptions> simulation)
    {
        m_world = world;
        m_sender = sender;
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
                if (entity is PlayerEntity player)
                {
                    Regenerate(player, now);
                }
            }
        }
    }

    private void Regenerate(PlayerEntity player, long now)
    {
        Regeneration regeneration = player.Regeneration;

        // Players are placed while the tick drains its commands, so this is the tick the player was placed on.
        if (player.NextHealthRegenerationMs == long.MinValue)
        {
            player.NextHealthRegenerationMs = now + regeneration.HealthIntervalMs;
            player.NextSpiritRegenerationMs = now + regeneration.SpiritIntervalMs;
            return;
        }

        if (now >= player.NextHealthRegenerationMs)
        {
            player.NextHealthRegenerationMs += regeneration.HealthIntervalMs;
            if (!player.IsDead && player.CurrentHealth < player.MaxHealth)
            {
                player.CurrentHealth = Math.Min(player.MaxHealth, player.CurrentHealth + regeneration.Health);
                if (player.Owner != default)
                {
                    m_sender.Send(
                        player.Owner,
                        new CharacterHealth((uint)player.CurrentHealth, (uint)player.MaxHealth));
                }
            }
        }

        if (now >= player.NextSpiritRegenerationMs)
        {
            player.NextSpiritRegenerationMs += regeneration.SpiritIntervalMs;
            if (!player.IsDead && player.CurrentSpirit < player.MaxSpirit)
            {
                player.CurrentSpirit = Math.Min(player.MaxSpirit, player.CurrentSpirit + regeneration.Spirit);
            }
        }
    }
}
}
