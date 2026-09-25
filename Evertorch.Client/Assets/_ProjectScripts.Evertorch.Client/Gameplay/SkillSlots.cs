using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The skill bar's slots (Prototype Content §4): each holds one skill, fixed by its ID, and shows once the server's
///     skill list has it.
/// </summary>
public static class SkillSlots
{
    private static readonly SkillDefinitionId[] Skills =
    {
        new("skill.strike"), new("skill.first_aid"), new("skill.focus")
    };

    public static int Count => Skills.Length;

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
}
}
