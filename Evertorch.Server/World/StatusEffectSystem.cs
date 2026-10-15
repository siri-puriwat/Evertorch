using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     Status effects on players and monsters (Gameplay Systems §9.1): a skill starts one or renews its duration, and it
///     ends at its time, on death, and with the character's time in the world; a monster holds ailments alone. Each tick
///     ends what is due, then drains poison, and the ailments stand in the state flags everyone who knows the body sees.
///     Every change to a player recalculates its statistics and tells its owner. Registered before
///     <see cref="CombatSystem" />, so a swing that begins in the tick an effect ends already has the statistics without
///     it.
/// </summary>
public sealed class StatusEffectSystem : ITickPhase
{
    private const int PoisonIntervalMs = 1000;
    private static readonly AilmentRules Ailments = new();

    private readonly WorldSimulation m_world;
    private readonly SessionRegistry m_sessions;
    private readonly ServerContent m_content;
    private readonly CharacterStats m_stats;
    private readonly MessageSender m_sender;
    private readonly ServerInstruments m_instruments;

    public StatusEffectSystem(
        WorldSimulation world,
        SessionRegistry sessions,
        ServerContent content,
        CharacterStats stats,
        MessageSender sender,
        ServerInstruments instruments)
    {
        m_world = world;
        m_sessions = sessions;
        m_content = content;
        m_stats = stats;
        m_sender = sender;
        m_instruments = instruments;
    }

    public TickPhase Phase => TickPhase.Combat;

    public void Execute(in TickContext context)
    {
        long now = m_world.NowMs;
        foreach (MapInstance map in m_world.Maps)
        {
            foreach (WorldEntity entity in map.Entities)
            {
                if (entity.StatusEffects.Count == 0)
                {
                    continue;
                }

                int ended = entity.EndStatusEffectsDueBy(now);
                if (ended > 0)
                {
                    m_instruments.RecordStatusEffects(StatusEffectChange.Ended, ended);
                    Recalculate(entity);
                }

                DrainPoison(map, entity, now, context.Tick);
            }
        }
    }

    /// <summary>
    ///     Starts <paramref name="status" /> on <paramref name="entity" /> at the strength of the level that cast it,
    ///     ending at <paramref name="endMs" />, or, when it is already active, replaces its strength and renews it to end
    ///     then; either way a player's statistics are derived again, so a recast at another level changes them at once
    ///     (Gameplay Systems §9.1). A status that sets an attack element first ends any other that does; poison drains
    ///     first a second after it starts.
    /// </summary>
    public void Apply(WorldEntity entity, StatusDefinitionId status, StatPercentages statPercent, long endMs)
    {
        m_content.StatusEffects.TryGetValue(status, out StatusEffectDefinition? definition);
        Element? attackElement = definition?.AttackElement;
        Ailment ailment = definition?.Ailment ?? Ailment.None;
        if (entity is MonsterEntity && ailment == Ailment.None)
        {
            throw new InvalidOperationException($"A monster holds ailments alone, not '{status}'.");
        }

        if (attackElement.HasValue)
        {
            int replaced = entity.EndAttackElementEffects(status);
            if (replaced > 0)
            {
                m_instruments.RecordStatusEffects(StatusEffectChange.Ended, replaced);
            }
        }

        long nextTickMs = ailment == Ailment.Poison ? m_world.NowMs + PoisonIntervalMs : long.MaxValue;
        bool isNew = entity.StartStatusEffect(
            new ActiveStatusEffect(status, statPercent, endMs, attackElement, ailment, nextTickMs));
        m_instruments.RecordStatusEffects(isNew ? StatusEffectChange.Started : StatusEffectChange.Renewed, 1);
        Recalculate(entity);
    }

    /// <summary>
    ///     A change of the worn weapon ends the status that set its attack element, a converter's (Gameplay Systems
    ///     §9.1).
    /// </summary>
    public void EndAttackElement(PlayerEntity player)
    {
        int ended = player.EndAttackElementEffects();
        if (ended > 0)
        {
            m_instruments.RecordStatusEffects(StatusEffectChange.Ended, ended);
            Recalculate(player);
        }
    }

    /// <summary>
    ///     Ends the ailments <paramref name="ended" /> names on <paramref name="entity" />, a hit's break or a cure
    ///     (Gameplay Systems §9.1), counted as <paramref name="change" />; returns how many ended.
    /// </summary>
    public int EndAilments(WorldEntity entity, Predicate<Ailment> ended, StatusEffectChange change)
    {
        int count = entity.EndAilments(ended);
        if (count > 0)
        {
            m_instruments.RecordStatusEffects(change, count);
            Recalculate(entity);
        }

        return count;
    }

    /// <summary>
    ///     Ends the ailments of <paramref name="cures" /> active on <paramref name="entity" /> (Gameplay Systems §11.2).
    /// </summary>
    public int Cure(WorldEntity entity, IReadOnlyList<Ailment> cures)
    {
        return cures.Count == 0 ? 0 : EndAilments(entity, cures.Contains, StatusEffectChange.Cured);
    }

    /// <summary>
    ///     Death ends every effect, a monster's ailments with a player's (Gameplay Systems §9.1).
    /// </summary>
    public void EndAll(WorldEntity entity)
    {
        int ended = entity.EndAllStatusEffects();
        if (ended > 0)
        {
            m_instruments.RecordStatusEffects(StatusEffectChange.Ended, ended);
            Recalculate(entity);
        }
    }

    // Poison drains every second from its start while the body keeps more than a quarter of its HP and more than the
    // loss, so it never kills; its schedule runs on through a tick it skips. A tick is passive: it logs no damage,
    // makes no grudge, and breaks no hold. Everyone who knows the body sees it, with no source.
    private void DrainPoison(MapInstance map, WorldEntity entity, long now, uint tick)
    {
        for (int index = 0; index < entity.StatusEffects.Count; index++)
        {
            ActiveStatusEffect effect = entity.StatusEffects[index];
            if (effect.Ailment != Ailment.Poison)
            {
                continue;
            }

            long next = effect.NextTickMs;
            while (next <= now && next < effect.EndMs)
            {
                next += PoisonIntervalMs;
                int loss = Ailments.PoisonLoss(entity.MaxHealth, entity is PlayerEntity);
                if (entity.IsDead || !Ailments.DoesPoisonTick(entity.CurrentHealth, entity.MaxHealth, loss))
                {
                    continue;
                }

                entity.CurrentHealth -= loss;
                TellPoison(map, entity, loss, tick);
            }

            entity.SetNextTick(index, next);
        }
    }

    private void TellPoison(MapInstance map, WorldEntity entity, int loss, uint tick)
    {
        var damage = new Damage(default, entity.Id, CombatResult.Poison, (uint)loss, tick, entity.SharedHealthPermille);
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Map == map && session.Knows(entity.Id))
            {
                m_sender.Send(session.Connection, damage);
            }
        }

        if (entity is PlayerEntity player)
        {
            m_sender.SendHealth(player);
        }
    }

    // The maximums can change with the statistics; the owner hears of new ones as of any other change of its HP or SP.
    private void Recalculate(WorldEntity entity)
    {
        if (!(entity is PlayerEntity player))
        {
            return;
        }

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
