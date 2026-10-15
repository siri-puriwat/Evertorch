using System;
using Evertorch.Game;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     Lands ailments for combat (Gameplay Systems §9.1): one <c>Next(10_000)</c> from the combat source for each source,
///     drawn before the refusal, the immunity, and the resistance are checked, so the draws depend on the attacker, the
///     hit, and whether the target lived, never on the target's state; and the break a landed hit makes of a freeze or a
///     sleep, before its riders roll.
/// </summary>
public sealed class AilmentRolls
{
    private const int ChanceRange = 10_000;
    private static readonly AilmentRules Rules = new();

    private readonly ServerContent m_content;
    private readonly StatusEffectSystem m_statusEffects;
    private readonly WorldSimulation m_world;
    private readonly IRandomSource m_random;
    private readonly ServerInstruments m_instruments;
    private readonly int m_chanceScale;

    public AilmentRolls(
        ServerContent content,
        StatusEffectSystem statusEffects,
        WorldSimulation world,
        IRandomSource random,
        ServerInstruments instruments,
        int chanceScale)
    {
        m_content = content;
        m_statusEffects = statusEffects;
        m_world = world;
        m_random = random;
        m_instruments = instruments;
        m_chanceScale = chanceScale;
    }

    /// <summary>
    ///     A landed hit (a hit or a critical, never a miss, an immune target, or a poison tick) wakes a sleeper and thaws
    ///     a frozen body. True when it broke one, so the same hit starts no hold.
    /// </summary>
    public bool Break(WorldEntity target)
    {
        return m_statusEffects.EndAilments(
            target,
            ailment => ailment == Ailment.Freeze || ailment == Ailment.Sleep,
            StatusEffectChange.Broken) > 0;
    }

    /// <summary>
    ///     A landed hit's ailments, in their order: the damage effect's rider, then, for a player's physical hit, its worn
    ///     cards' chances in <see cref="Ailment" /> order, each lasting its status's duration.
    /// </summary>
    public void RollRiders(WorldEntity source, WorldEntity target, AilmentRider? rider, bool isPhysical, bool hasBroken)
    {
        if (rider != null)
        {
            Roll(source, target, rider.Status, rider.ChanceBasisPoints, rider.DurationMs, hasBroken);
        }

        if (!(source is PlayerEntity player) || !isPhysical)
        {
            return;
        }

        CardBonus cards = player.CardBonus;
        for (Ailment ailment = Ailment.Poison; ailment <= Ailment.Blind; ailment++)
        {
            int chance = Math.Min(ChanceRange, cards.ChanceToInflict(ailment));
            if (chance > 0 && TryStatusOf(ailment, out StatusEffectDefinition? status))
            {
                Roll(source, target, status!.Id, chance, status.DurationMs, hasBroken);
            }
        }
    }

    /// <summary>
    ///     Draws for one ailment of <paramref name="status" /> on <paramref name="target" /> and lands it unless it is
    ///     refused, immune, or resisted; the outcome is counted either way.
    /// </summary>
    public AilmentOutcome Roll(
        WorldEntity source,
        WorldEntity target,
        StatusDefinitionId status,
        int chanceBasisPoints,
        int durationMs,
        bool hasBroken = false)
    {
        int draw = m_random.Next(ChanceRange);
        Ailment ailment = m_content.StatusEffects[status].Ailment;
        AilmentOutcome outcome = Judge(source, target, ailment, chanceBasisPoints, durationMs, hasBroken, draw,
            out int lasting);
        if (outcome == AilmentOutcome.Landed)
        {
            m_statusEffects.Apply(target, status, default, m_world.NowMs + lasting);
        }

        m_instruments.RecordAilment(ailment, outcome);
        return outcome;
    }

    private static bool IsHold(Ailment ailment)
    {
        return ailment == Ailment.Stun || ailment == Ailment.Freeze || ailment == Ailment.Sleep;
    }

    private static bool IsUndead(WorldEntity target)
    {
        return target switch
        {
            MonsterEntity monster => monster.Definition.Element == Element.Undead
                || monster.Definition.Race == Race.Undead,
            PlayerEntity player => player.ArmorElement == Element.Undead,
            _ => false
        };
    }

    private static int LevelOf(WorldEntity entity)
    {
        return entity switch
        {
            MonsterEntity monster => monster.Definition.Level,
            PlayerEntity player => player.Level,
            _ => 1
        };
    }

    // A player resists by VIT, INT, AGI, or soft magic defense, as the ailment asks, and by its armor cards; a monster
    // by its level alone. A higher-level source cuts either.
    private static int ResistanceOf(WorldEntity target, Ailment ailment, int advantage)
    {
        if (!(target is PlayerEntity player))
        {
            return Rules.MonsterResistance(LevelOf(target), advantage);
        }

        return Rules.PlayerResistance(StatisticAgainst(player, ailment), advantage);
    }

    /// <summary>The statistic a player resists <paramref name="ailment" /> by.</summary>
    public static int StatisticAgainst(PlayerEntity player, Ailment ailment)
    {
        PrimaryStats stats = player.EffectivePrimary;
        return ailment switch
        {
            Ailment.Poison => stats.Vit,
            Ailment.Stun => stats.Vit,
            Ailment.Sleep => stats.Agi,
            Ailment.Freeze => player.Stats.SoftMagicDefense,
            _ => stats.Int
        };
    }

    private AilmentOutcome Judge(
        WorldEntity source,
        WorldEntity target,
        Ailment ailment,
        int chanceBasisPoints,
        int durationMs,
        bool hasBroken,
        int draw,
        out int lasting)
    {
        lasting = 0;
        if (target.IsDead
            || target.HasAilment(ailment)
            || (IsHold(ailment) && (target.IsHeld || hasBroken)))
        {
            return AilmentOutcome.Refused;
        }

        if ((target is MonsterEntity monster && monster.Definition.IsBoss)
            || (ailment == Ailment.Freeze && IsUndead(target)))
        {
            return AilmentOutcome.Immune;
        }

        int advantage = Rules.LevelAdvantage(LevelOf(source), LevelOf(target));
        int resistance = ResistanceOf(target, ailment, advantage);
        int cards = target is PlayerEntity player ? Rules.CardResistance(player.CardBonus.ResistanceTo(ailment)) : 0;
        if (draw >= Rules.LandingChance(chanceBasisPoints, m_chanceScale, resistance, cards))
        {
            return AilmentOutcome.Resisted;
        }

        lasting = Rules.Duration(durationMs, resistance, cards);
        return AilmentOutcome.Landed;
    }

    private bool TryStatusOf(Ailment ailment, out StatusEffectDefinition? status)
    {
        foreach (StatusEffectDefinition candidate in m_content.StatusEffects.Values)
        {
            if (candidate.Ailment == ailment)
            {
                status = candidate;
                return true;
            }
        }

        status = null;
        return false;
    }
}
}
