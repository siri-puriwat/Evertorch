namespace Evertorch.Persistence
{
/// <summary>
///     What a party change does (Persistence §5): a creation by the first accepted invite, a join, a departure, a
///     removal by the leader, or a new leader.
/// </summary>
public enum PartyChangeKind
{
    Create,
    Join,
    Leave,
    Kick,
    Lead
}
}
