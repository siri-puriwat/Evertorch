using Evertorch.Game;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     The one path that derives a character's statistics (Gameplay Systems §2): its level, its primary statistics
///     with its status effects' percentages, and its job's tuning, through the character rules.
/// </summary>
public sealed class CharacterStats
{
    private readonly ICharacterRules m_rules;

    public CharacterStats(ICharacterRules rules)
    {
        m_rules = rules;
    }

    public DerivedStats Calculate(JobDefinition job, int level, PrimaryStats primary)
    {
        var build = new CharacterBuild(
            level,
            primary,
            job.HealthBase,
            job.HealthPerLevel,
            job.SpiritBase,
            job.SpiritPerLevel,
            job.UnarmedAttackSpeedPenalty,
            (float)job.BaseSpeed);
        return m_rules.CalculateDerivedStats(build);
    }

    public Regeneration CalculateRegeneration(PrimaryStats primary, DerivedStats stats)
    {
        return m_rules.CalculateRegeneration(primary, stats);
    }

    /// <summary>
    ///     Derives <paramref name="player" />'s statistics again from its current level, primary statistics, and
    ///     status effects.
    /// </summary>
    public void Recalculate(PlayerEntity player, JobDefinition job)
    {
        var percent = new StatPercentages(0, 0, 0, 0, 0, 0);
        foreach (ActiveStatusEffect effect in player.StatusEffects)
        {
            percent = percent.Plus(effect.StatPercent);
        }

        PrimaryStats primary = m_rules.ApplyStatPercent(player.Primary, percent);
        DerivedStats stats = Calculate(job, player.Level, primary);
        player.ApplyStats(stats, m_rules.CalculateRegeneration(primary, stats));
    }
}
}
