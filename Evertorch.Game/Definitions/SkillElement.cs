namespace Evertorch.Game
{
/// <summary>
///     The element a damage skill strikes with (Gameplay Systems §9): the caster's attack element, Neutral for a monster,
///     or one of the ten, in the order of <see cref="Element" />.
/// </summary>
public enum SkillElement
{
    Weapon,
    Neutral,
    Water,
    Earth,
    Fire,
    Wind,
    Poison,
    Holy,
    Shadow,
    Ghost,
    Undead
}
}
