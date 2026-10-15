using Evertorch.Game;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     The one path that derives a character's statistics (Gameplay Systems §2): its level, its primary statistics
///     with its status effects' percentages and its equipment's bonuses, its weapon's attack-speed penalty with what a
///     shield adds, and its job's tuning, through the character rules.
/// </summary>
public sealed class CharacterStats
{
    private readonly ICharacterRules m_rules;

    public CharacterStats(ICharacterRules rules)
    {
        m_rules = rules;
    }

    /// <param name="weapon">The worn weapon, whose penalty replaces the job's unarmed one; null when unarmed.</param>
    /// <param name="addedPenalty">What the other worn pieces add to that penalty, a shield's (Gameplay Systems §7).</param>
    public DerivedStats Calculate(
        JobDefinition job,
        int level,
        PrimaryStats primary,
        ItemEquipment? weapon = null,
        int addedPenalty = 0)
    {
        var build = new CharacterBuild(
            level,
            primary,
            job.HealthBase,
            job.HealthPerLevel,
            job.SpiritBase,
            job.SpiritPerLevel,
            (weapon?.AttackSpeedPenalty ?? job.UnarmedAttackSpeedPenalty) + addedPenalty,
            (float)job.BaseSpeed);
        return m_rules.CalculateDerivedStats(build);
    }

    public Regeneration CalculateRegeneration(PrimaryStats primary, DerivedStats stats)
    {
        return m_rules.CalculateRegeneration(primary, stats);
    }

    /// <summary>
    ///     Derives <paramref name="player" />'s statistics again from its current level, primary statistics, status
    ///     effects, and equipment.
    /// </summary>
    public void Recalculate(PlayerEntity player, JobDefinition job)
    {
        var percent = new StatPercentages(0, 0, 0, 0, 0, 0);
        foreach (ActiveStatusEffect effect in player.StatusEffects)
        {
            percent = percent.Plus(effect.StatPercent);
        }

        // A status effect's percentage is of the statistic without the equipment's bonuses (equipment research note),
        // and the worn cards add theirs as the pieces do.
        CardBonus cards = player.CardBonus;
        WornEquipment worn = player.Equipment;
        PrimaryStats primary = Plus(Plus(m_rules.ApplyStatPercent(player.Primary, percent), worn.Bonus), cards.Stats);
        DerivedStats stats = WithMaxima(
            Calculate(job, player.Level, primary, player.Weapon, worn.AttackSpeedPenalty),
            cards);
        player.ApplyStats(stats, m_rules.CalculateRegeneration(primary, stats));
        player.EffectivePrimary = primary;
    }

    internal static PrimaryStats Plus(PrimaryStats stats, PrimaryStats? bonus)
    {
        if (!bonus.HasValue)
        {
            return stats;
        }

        PrimaryStats added = bonus.Value;
        return new PrimaryStats(
            stats.Str + added.Str,
            stats.Agi + added.Agi,
            stats.Vit + added.Vit,
            stats.Int + added.Int,
            stats.Dex + added.Dex,
            stats.Luk + added.Luk);
    }

    // The cards' maximum HP and SP come after the VIT and INT scaling (Gameplay Systems §2).
    private static DerivedStats WithMaxima(DerivedStats stats, CardBonus cards)
    {
        if (cards.MaxHp == 0 && cards.MaxSp == 0)
        {
            return stats;
        }

        return new DerivedStats(
            stats.MaxHp + cards.MaxHp,
            stats.MaxSp + cards.MaxSp,
            stats.PhysicalAttack,
            stats.MagicalAttack,
            stats.SoftDefense,
            stats.SoftMagicDefense,
            stats.Hit,
            stats.Flee,
            stats.Critical,
            stats.PerfectDodge,
            stats.AttackSpeed,
            stats.MovementSpeed,
            stats.VariableCastPermille,
            stats.FixedCastPermille);
    }
}
}
