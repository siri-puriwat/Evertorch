using System;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     An outcome's word in the counters and the logs, as <c>refine_attempts</c> stores it (Persistence §4).
/// </summary>
public static class RefineOutcomes
{
    public static string WordOf(RefineOutcomeKind outcome)
    {
        return outcome switch
        {
            RefineOutcomeKind.Refined => "refined",
            RefineOutcomeKind.Destroyed => "destroyed",
            RefineOutcomeKind.Lowered => "lowered",
            RefineOutcomeKind.Unchanged => "unchanged",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "No such outcome.")
        };
    }
}
}
