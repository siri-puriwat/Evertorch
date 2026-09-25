using Evertorch.Game;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     The one path that derives a character's statistics (Gameplay Systems §2): its level, its primary statistics,
///     and its job's tuning, through the character rules.
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
    ///     Derives <paramref name="player" />'s statistics again from its current level and primary statistics.
    /// </summary>
    public void Recalculate(PlayerEntity player, JobDefinition job)
    {
        DerivedStats stats = Calculate(job, player.Level, player.Primary);
        player.ApplyStats(stats, m_rules.CalculateRegeneration(player.Primary, stats));
    }
}
}
