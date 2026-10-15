using System.Collections.Generic;
using System.Text;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The six ailments as the state flags carry them (Gameplay Systems §9.1), always in the same order, poison, stun,
///     freeze, sleep, silence, and blind, and the words the client draws them with (Prototype Content §2).
/// </summary>
public static class Ailments
{
    public const EntityStateFlags All = EntityStateFlags.Poisoned
        | EntityStateFlags.Stunned
        | EntityStateFlags.Frozen
        | EntityStateFlags.Asleep
        | EntityStateFlags.Silenced
        | EntityStateFlags.Blinded;

    /// <summary>
    ///     The ailments that hold a body: no step, swing, or cast.
    /// </summary>
    public const EntityStateFlags Holds = EntityStateFlags.Stunned | EntityStateFlags.Frozen | EntityStateFlags.Asleep;

    /// <summary>
    ///     The ailments that end a cast under way.
    /// </summary>
    public const EntityStateFlags EndCasts = Holds | EntityStateFlags.Silenced;

    // A party status's bits start at poison, two below the state flags' (Network Protocol §9).
    private const int PartyShift = 2;

    private static readonly Entry[] Entries =
    {
        new(EntityStateFlags.Poisoned, "Poisoned", "Psn"),
        new(EntityStateFlags.Stunned, "Stunned", "Stn"),
        new(EntityStateFlags.Frozen, "Frozen", "Frz"),
        new(EntityStateFlags.Asleep, "Asleep", "Slp"),
        new(EntityStateFlags.Silenced, "Silenced", "Sil"),
        new(EntityStateFlags.Blinded, "Blinded", "Bld")
    };

    /// <summary>
    ///     The ailments among <paramref name="flags" />, one flag each, in order.
    /// </summary>
    public static IEnumerable<EntityStateFlags> Of(EntityStateFlags flags)
    {
        foreach (Entry entry in Entries)
        {
            if ((flags & entry.Flag) != 0)
            {
                yield return entry.Flag;
            }
        }
    }

    /// <summary>
    ///     The state flags of a party member's ailment bits.
    /// </summary>
    public static EntityStateFlags FromParty(byte ailments)
    {
        return (EntityStateFlags)(ailments << PartyShift) & All;
    }

    /// <summary>
    ///     "Frozen", "Poisoned", and the others; empty for anything else.
    /// </summary>
    public static string Name(EntityStateFlags ailment)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Flag == ailment)
            {
                return entry.Name;
            }
        }

        return string.Empty;
    }

    /// <summary>
    ///     The ailments' names joined, "Poisoned · Frozen"; empty when there are none.
    /// </summary>
    public static string Names(EntityStateFlags flags)
    {
        return Join(flags, false, " · ");
    }

    /// <summary>
    ///     The ailments' short words for a party row, "Psn Stn"; empty when there are none.
    /// </summary>
    public static string ShortNames(EntityStateFlags flags)
    {
        return Join(flags, true, " ");
    }

    private static string Join(EntityStateFlags flags, bool isShort, string separator)
    {
        if ((flags & All) == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (Entry entry in Entries)
        {
            if ((flags & entry.Flag) == 0)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append(separator);
            }

            text.Append(isShort ? entry.Short : entry.Name);
        }

        return text.ToString();
    }

    private readonly struct Entry
    {
        public Entry(EntityStateFlags flag, string name, string shortName)
        {
            Flag = flag;
            Name = name;
            Short = shortName;
        }

        public EntityStateFlags Flag { get; }

        public string Name { get; }

        public string Short { get; }
    }
}
}
