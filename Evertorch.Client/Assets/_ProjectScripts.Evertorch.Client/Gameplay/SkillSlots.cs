using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The skill bar's slots (Prototype Content §4), following the server's skill list: slots 1 to 3 hold the base
///     job's tree, the first three entries, slots 4 and 5 each a potion, which shows while the inventory has one, and
///     slots 6 to 8 a first job's own tree, the entries after the base job's.
/// </summary>
public static class SkillSlots
{
    public const int Count = 8;

    /// <summary>
    ///     The first of the slots that hold a first job's own tree, which the gamepad's second page reaches.
    /// </summary>
    public const int FirstOwnSlot = 6;

    private const int BaseSlots = 3;

    private static readonly ItemDefinitionId[] Items =
    {
        new("item.consumable.minor_health"), new("item.consumable.minor_mana")
    };

    /// <summary>
    ///     The skill in <paramref name="slot" />, numbered from 1 as its key is, from <paramref name="skills" />.
    /// </summary>
    public static bool TryGetSkill(IReadOnlyList<SkillListEntry> skills, int slot, out SkillDefinitionId skill)
    {
        int index = slot >= 1 && slot <= BaseSlots
            ? slot - 1
            : slot >= FirstOwnSlot && slot <= Count
                ? BaseSlots + slot - FirstOwnSlot
                : -1;
        if (index < 0 || index >= skills.Count)
        {
            skill = default;
            return false;
        }

        skill = skills[index].Skill;
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="skills" /> reach past the base job's tree, so slots 6 to 8 and the gamepad's second
    ///     page are in use.
    /// </summary>
    public static bool HasOwnSkills(IReadOnlyList<SkillListEntry> skills)
    {
        return skills.Count > BaseSlots;
    }

    /// <summary>
    ///     The potion in <paramref name="slot" />, numbered from 1 as its key is.
    /// </summary>
    public static bool TryGetItem(int slot, out ItemDefinitionId item)
    {
        int index = slot - BaseSlots - 1;
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
