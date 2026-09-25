using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One status effect on the character and the time it has left (Network Protocol §9).
/// </summary>
public readonly struct StatusEffectEntry
{
    public StatusEffectEntry(StatusDefinitionId status, uint remainingMs)
    {
        Status = status;
        RemainingMs = remainingMs;
    }

    public StatusDefinitionId Status { get; }

    /// <summary>
    ///     What was left of the effect when the server sent the list.
    /// </summary>
    public uint RemainingMs { get; }
}
}
