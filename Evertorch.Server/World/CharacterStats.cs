using Evertorch.Game;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     The one path that derives a character's statistics (Gameplay Systems §2): its level, its primary statistics
///     with its status effects' percentages and its equipment's bonuses, its weapon's attack-speed penalty, and its
///     job's tuning, through the character rules.
/// </summary>
public sealed class CharacterStats
{
    private readonly ICharacterRules m_rules;

    public CharacterStats(ICharacterRules rules)
    {
        m_rules = rules;
    }

    /// <param name="weapon">The worn weapon, whose penalty replaces the job's unarmed one; null when unarmed.</param>
    public DerivedStats Calculate(JobDefinition job, int level, PrimaryStats primary, ItemEquipment? weapon = null)
    {
        var build = new CharacterBuild(
            level,
            primary,
            job.HealthBase,
            job.HealthPerLevel,
            job.SpiritBase,
            job.SpiritPerLevel,
            weapon?.AttackSpeedPenalty ?? job.UnarmedAttackSpeedPenalty,
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

        // A status effect's percentage is of the statistic without the equipment's bonuses (equipment research note).
        PrimaryStats primary = WithBonus(
            WithBonus(m_rules.ApplyStatPercent(player.Primary, percent), player.Weapon),
            player.Armor);
        DerivedStats stats = Calculate(job, player.Level, primary, player.Weapon);
        player.ApplyStats(stats, m_rules.CalculateRegeneration(primary, stats));
    }

    private static PrimaryStats WithBonus(PrimaryStats stats, ItemEquipment? worn)
    {
        if (worn == null)
        {
            return stats;
        }

        PrimaryStats bonus = worn.Bonus;
        return new PrimaryStats(
            stats.Str + bonus.Str,
            stats.Agi + bonus.Agi,
            stats.Vit + bonus.Vit,
            stats.Int + bonus.Int,
            stats.Dex + bonus.Dex,
            stats.Luk + bonus.Luk);
    }
}
}
