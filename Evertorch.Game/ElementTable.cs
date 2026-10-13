namespace Evertorch.Game
{
/// <summary>
///     Evertorch's table of what an attack of each element deals a defender of each element, in percent, at one level
///     (Gameplay Systems §3). It is written as the relations of the elements, races, and sizes research note, the
///     first that matches winning, rather than as an array, and it lives here because the client's target frame reads
///     it beside the server's rules.
/// </summary>
public static class ElementTable
{
    public static int Percent(Element attack, Element defense)
    {
        if (attack == defense)
        {
            return OnItself(attack);
        }

        if (IsShadowOrUndead(attack) && IsShadowOrUndead(defense))
        {
            return 0;
        }

        if (IsNatural(attack) && IsNatural(defense))
        {
            return AroundTheCycle(attack, defense);
        }

        if (IsHolyOrShadow(attack) && IsHolyOrShadow(defense))
        {
            return 150;
        }

        if ((attack == Element.Fire || attack == Element.Holy) && defense == Element.Undead)
        {
            return 150;
        }

        if (IsNeutralOrGhost(attack) && IsNeutralOrGhost(defense))
        {
            return 50;
        }

        if (attack == Element.Poison && IsNatural(defense))
        {
            return 125;
        }

        if (attack == Element.Poison && defense != Element.Neutral)
        {
            return 50;
        }

        return 100;
    }

    /// <summary>
    ///     The element a skill strikes with: the attacker's attack element for a skill of the weapon's, else the
    ///     skill's own (Gameplay Systems §9).
    /// </summary>
    public static Element Of(SkillElement skill, Element attackElement)
    {
        return skill switch
        {
            SkillElement.Weapon => attackElement,
            SkillElement.Neutral => Element.Neutral,
            SkillElement.Water => Element.Water,
            SkillElement.Earth => Element.Earth,
            SkillElement.Fire => Element.Fire,
            SkillElement.Wind => Element.Wind,
            SkillElement.Poison => Element.Poison,
            SkillElement.Holy => Element.Holy,
            SkillElement.Shadow => Element.Shadow,
            SkillElement.Ghost => Element.Ghost,
            _ => Element.Undead
        };
    }

    private static int OnItself(Element element)
    {
        switch (element)
        {
            case Element.Neutral:
                return 100;
            case Element.Ghost:
                return 150;
            case Element.Poison:
            case Element.Holy:
            case Element.Shadow:
            case Element.Undead:
                return 0;
            default:
                return 25;
        }
    }

    // The natural cycle, Water to Fire to Earth to Wind and back to Water: 150 to the next, 75 to the one before, and
    // 100 across the cycle.
    private static int AroundTheCycle(Element attack, Element defense)
    {
        if (defense == Next(attack))
        {
            return 150;
        }

        return attack == Next(defense) ? 75 : 100;
    }

    private static Element Next(Element natural)
    {
        switch (natural)
        {
            case Element.Water:
                return Element.Fire;
            case Element.Fire:
                return Element.Earth;
            case Element.Earth:
                return Element.Wind;
            default:
                return Element.Water;
        }
    }

    private static bool IsNatural(Element element)
    {
        return element == Element.Water
            || element == Element.Earth
            || element == Element.Fire
            || element == Element.Wind;
    }

    private static bool IsShadowOrUndead(Element element)
    {
        return element == Element.Shadow || element == Element.Undead;
    }

    private static bool IsHolyOrShadow(Element element)
    {
        return element == Element.Holy || element == Element.Shadow;
    }

    private static bool IsNeutralOrGhost(Element element)
    {
        return element == Element.Neutral || element == Element.Ghost;
    }
}
}
