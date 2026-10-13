namespace Evertorch.Game
{
/// <summary>
///     A monster's race (Gameplay Systems §10), which a card may deal more damage to; players are Humanoid. Content
///     names it in camelCase, and it never travels on the wire.
/// </summary>
public enum Race
{
    Formless,
    Undead,
    Beast,
    Plant,
    Insect,
    Fish,
    Demon,
    Humanoid,
    Angel,
    Dragon
}
}
