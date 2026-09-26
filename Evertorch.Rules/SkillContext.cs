using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     One cast's inputs. A damage skill also needs the hit and damage inputs of its caster against its target; a heal
///     and the cast time need neither.
/// </summary>
public readonly struct SkillContext
{
    public SkillContext(SkillDefinition skill, AttackerKind caster, int variableCastPermille)
    {
        if (variableCastPermille < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(variableCastPermille), "Cast share cannot be negative.");
        }

        Skill = skill ?? throw new ArgumentNullException(nameof(skill));
        Caster = caster;
        VariableCastPermille = variableCastPermille;
        Hit = null;
        Damage = null;
        Magic = null;
    }

    public SkillContext(
        SkillDefinition skill,
        AttackerKind caster,
        int variableCastPermille,
        HitContext hit,
        DamageContext damage)
        : this(skill, caster, variableCastPermille)
    {
        Hit = hit;
        Damage = damage;
    }

    public SkillContext(SkillDefinition skill, AttackerKind caster, int variableCastPermille, MagicDamageContext magic)
        : this(skill, caster, variableCastPermille)
    {
        Magic = magic;
    }

    public SkillDefinition Skill { get; }

    public AttackerKind Caster { get; }

    /// <summary>
    ///     A character's remaining share of variable cast time, in thousandths; monsters ignore it.
    /// </summary>
    public int VariableCastPermille { get; }

    public HitContext? Hit { get; }

    public DamageContext? Damage { get; }

    /// <summary>A magical damage skill's inputs, in place of the hit and damage ones.</summary>
    public MagicDamageContext? Magic { get; }
}
}
