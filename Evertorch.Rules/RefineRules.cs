using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     Refining as the refining research note describes it (Gameplay Systems §11.1, §11.6): one draw decides an attempt,
///     and an item's level gives its table's bonus. Nothing here draws or changes an item.
/// </summary>
public static class RefineRules
{
    /// <summary>
    ///     An attempt draws one number below this, from 0 to 99, even at a chance of 100 %.
    /// </summary>
    public const int DrawRange = 100;

    /// <summary>
    ///     What one attempt from <paramref name="level" /> does with <paramref name="draw" />: a draw below the next
    ///     level's chance raises the item one level; otherwise the table's failure destroys it, lowers it by its levels
    ///     to +0 at the lowest, or leaves it as it was, as a fall from +0 does.
    /// </summary>
    public static RefineResolution Resolve(RefineTableDefinition table, int level, int draw)
    {
        if (table == null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (level < 0 || level >= table.LastLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Only a level below the table's last refines.");
        }

        if (draw < 0 || draw >= DrawRange)
        {
            throw new ArgumentOutOfRangeException(nameof(draw), draw, "A draw is a number from 0 to 99.");
        }

        if (draw < table.Steps[level].Chance)
        {
            return new RefineResolution(RefineOutcomeKind.Refined, level + 1);
        }

        switch (table.OnFailure)
        {
            case RefineFailure.Destroy:
                return new RefineResolution(RefineOutcomeKind.Destroyed, 0);
            case RefineFailure.Downgrade:
                int lowered = Math.Max(0, level - table.DowngradeBy);
                return lowered < level
                    ? new RefineResolution(RefineOutcomeKind.Lowered, lowered)
                    : new RefineResolution(RefineOutcomeKind.Unchanged, level);
            case RefineFailure.Keep:
                return new RefineResolution(RefineOutcomeKind.Unchanged, level);
            default:
                throw new ArgumentOutOfRangeException(nameof(table), table.OnFailure, "A table needs a failure.");
        }
    }

    /// <summary>
    ///     The bonus an item has at <paramref name="level" />: none at +0 or without a table, its table's at that level,
    ///     and the table's last level's past the table's end, which only changed content or a hand-edited row leaves.
    /// </summary>
    public static int BonusAt(RefineTableDefinition? table, int level)
    {
        if (table == null || level <= 0 || table.LastLevel == 0)
        {
            return 0;
        }

        return table.Steps[Math.Min(level, table.LastLevel) - 1].Bonus;
    }

    /// <summary>
    ///     The chance, in percent, of an attempt from <paramref name="level" />; 0 at or past the table's last level,
    ///     where nothing refines.
    /// </summary>
    public static int ChanceFrom(RefineTableDefinition table, int level)
    {
        return table == null || level < 0 || level >= table.LastLevel ? 0 : table.Steps[level].Chance;
    }
}
}
