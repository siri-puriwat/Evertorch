using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The skill bar's slots (Prototype Content §4): slots 1 to 3 each hold a skill, fixed by its ID, which shows once
///     the server's skill list has it; slots 4 and 5 each hold a potion, which shows while the inventory has one.
/// </summary>
public static class SkillSlots
{
    private static readonly SkillDefinitionId[] Skills =
    {
        new("skill.strike"), new("skill.first_aid"), new("skill.focus")
    };

    private static readonly ItemDefinitionId[] Items =
    {
        new("item.consumable.minor_health"), new("item.consumable.minor_mana")
    };

    public static int Count => Skills.Length + Items.Length;

    /// <summary>
    ///     The skill in <paramref name="slot" />, numbered from 1 as its key is.
    /// </summary>
    public static bool TryGetSkill(int slot, out SkillDefinitionId skill)
    {
        if (slot < 1 || slot > Skills.Length)
        {
            skill = default;
            return false;
        }

        skill = Skills[slot - 1];
        return true;
    }

    /// <summary>
    ///     The potion in <paramref name="slot" />, numbered from 1 as its key is.
    /// </summary>
    public static bool TryGetItem(int slot, out ItemDefinitionId item)
    {
        int index = slot - Skills.Length - 1;
        if (index < 0 || index >= Items.Length)
        {
            item = default;
            return false;
        }

        item = Items[index];
        return true;
    }
}
}
