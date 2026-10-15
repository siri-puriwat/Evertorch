using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     What a press says while an ailment refuses it (Prototype Content §2): "You can't move while frozen.", "You can't
///     use skills while silenced.", "You can't use items while stunned.". The client refuses what the server would, so
///     nothing is sent for it.
/// </summary>
public static class AilmentMessages
{
    /// <summary>
    ///     Why <paramref name="act" /> is refused under <paramref name="flags" />; null when nothing refuses it. A hold
    ///     refuses every act, the first of stun, freeze, and sleep naming it; silence refuses skills alone.
    /// </summary>
    public static string? Refusal(EntityStateFlags flags, HeldAct act)
    {
        string? state = (flags & EntityStateFlags.Stunned) != 0
            ? "stunned"
            : (flags & EntityStateFlags.Frozen) != 0
                ? "frozen"
                : (flags & EntityStateFlags.Asleep) != 0
                    ? "asleep"
                    : act == HeldAct.UseSkills && (flags & EntityStateFlags.Silenced) != 0
                        ? "silenced"
                        : null;
        return state == null ? null : $"You can't {Verb(act)} while {state}.";
    }

    private static string Verb(HeldAct act)
    {
        return act switch
        {
            HeldAct.Move => "move",
            HeldAct.Attack => "attack",
            HeldAct.PickUp => "pick things up",
            HeldAct.Talk => "talk",
            HeldAct.UseItems => "use items",
            HeldAct.Trade => "trade",
            _ => "use skills"
        };
    }
}

/// <summary>
///     What the player tried while an ailment may refuse it.
/// </summary>
public enum HeldAct
{
    Move,
    Attack,
    PickUp,
    Talk,
    UseItems,
    UseSkills,
    Trade
}
}
