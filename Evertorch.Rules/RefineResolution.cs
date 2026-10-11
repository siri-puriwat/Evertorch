using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     What one refine did: its outcome and the level it left, 0 for a destroyed item.
/// </summary>
public readonly struct RefineResolution
{
    public RefineResolution(RefineOutcomeKind outcome, int level)
    {
        Outcome = outcome;
        Level = level;
    }

    public RefineOutcomeKind Outcome { get; }

    public int Level { get; }
}
}
