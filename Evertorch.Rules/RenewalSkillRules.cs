using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     Cast times and skill effects as the skills research note describes them (Gameplay Systems §9). Damage goes
///     through the combat rules with the skill's ratio; nothing here changes an entity.
/// </summary>
public sealed class RenewalSkillRules : ISkillRules
{
    private const int FullCastPermille = 1000;

    public CastTiming CalculateCastTiming(SkillContext context)
    {
        SkillLevel values = context.Values;
        long variable = context.Caster == AttackerKind.Character
            ? (long)values.VariableCastMs * context.VariableCastPermille / FullCastPermille
            : values.VariableCastMs;
        return new CastTiming(
            (int)Math.Min(int.MaxValue, values.FixedCastMs + variable),
            values.AfterCastDelayMs,
            values.CooldownMs);
    }

    public SkillResolution Resolve(SkillContext context)
    {
        if (!context.Skill.HasEffect)
        {
            throw new ArgumentException($"Skill {context.Skill.Id} has no effect.", nameof(context));
        }

        SkillEffect effect = context.Values.Effect;
        switch (effect.Kind)
        {
            case SkillEffectKind.Heal:
                return new SkillResolution(SkillResult.Healed, effect.HealHp);
            case SkillEffectKind.Damage when context.Skill.DamageType == SkillDamageType.Magical:
                return ResolveMagic(context, effect.DamageRatioPercent);
            case SkillEffectKind.Damage:
                return ResolveDamage(context, effect.DamageRatioPercent);
            default:
                throw new ArgumentException($"Skill {context.Skill.Id} has an unknown effect.", nameof(context));
        }
    }

    // Magic skips the hit roll, so it always lands, and never crits.
    private static SkillResolution ResolveMagic(SkillContext context, int ratioPercent)
    {
        if (!context.Magic.HasValue)
        {
            throw new ArgumentException($"Magic skill {context.Skill.Id} needs magic damage inputs.", nameof(context));
        }

        MagicDamageContext magic = context.Magic.Value.ForSkill(ratioPercent);
        return new SkillResolution(SkillResult.Hit, new RenewalCombatRules().CalculateMagicDamage(magic).Amount);
    }

    private static SkillResolution ResolveDamage(SkillContext context, int ratioPercent)
    {
        if (!context.Hit.HasValue || !context.Damage.HasValue)
        {
            throw new ArgumentException($"Damage skill {context.Skill.Id} needs hit and damage inputs.",
                nameof(context));
        }

        // A skill never crits: with the critical chance taken as 0 the roll still draws what a basic attack draws.
        HitContext hit = context.Hit.Value.WithoutCritical();
        var combat = new RenewalCombatRules();
        HitResult result = combat.CalculateHit(hit);
        if (!result.DealsDamage)
        {
            return new SkillResolution(
                result.Outcome == HitOutcome.PerfectDodge ? SkillResult.PerfectDodge : SkillResult.Miss,
                0);
        }

        // Copied through the context's own method, so every input of a basic attack, a refine's flat attack among them,
        // reaches the skill.
        DamageContext damage = context.Damage.Value.ForSkill(ratioPercent);
        return new SkillResolution(SkillResult.Hit, combat.CalculateDamage(damage).Amount);
    }
}
}
