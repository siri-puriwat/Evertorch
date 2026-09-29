namespace Evertorch.Game
{
public enum SkillTargetType
{
    Enemy,
    Self,

    /// <summary>
    ///     The caster or another live player it knows (Gameplay Systems §9); only a heal may name it.
    /// </summary>
    Ally
}
}
