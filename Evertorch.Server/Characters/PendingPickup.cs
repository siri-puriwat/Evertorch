namespace Evertorch.Server
{
/// <summary>
///     A pickup whose commit has not settled: the reserved drop and the command that asked for it.
/// </summary>
public sealed class PendingPickup
{
    public PendingPickup(ItemDropEntity drop, uint commandSequence)
    {
        Drop = drop;
        CommandSequence = commandSequence;
    }

    public ItemDropEntity Drop { get; }

    public uint CommandSequence { get; }
}
}
