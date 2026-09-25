namespace Evertorch.Rules
{
public interface ISkillRules
{
    CastTiming CalculateCastTiming(SkillContext context);

    SkillResolution Resolve(SkillContext context);
}
}
