namespace Evertorch.Persistence
{
/// <summary>
///     How a party change ended (Persistence §5). Every refusal is a status, never a key violation, and a change whose
///     end state already holds is <see cref="Committed" />, since the writer retries a commit after the database fails.
/// </summary>
public enum PartyChangeStatus
{
    Committed,
    NoSuchParty,
    AlreadyInParty,
    PartyFull,
    SameAccount,
    NotTheLeader,
    NotAMember
}
}
