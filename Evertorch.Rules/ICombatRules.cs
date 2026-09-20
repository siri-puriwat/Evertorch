using Evertorch.Game;

namespace Evertorch.Rules
{
public interface ICombatRules
{
    AttackTiming CalculateAttackTiming(AttackContext context);

    HitResult CalculateHit(HitContext context);

    DamageResult CalculateDamage(DamageContext context);
}
}
