namespace Evertorch.Persistence
{
/// <summary>
///     The status of a party change and the party as it stands after it: null when it disbanded the party or there is
///     none.
/// </summary>
public sealed class PartyChangeResult
{
    public PartyChangeResult(PartyChangeStatus status, StoredParty? party)
    {
        Status = status;
        Party = party;
    }

    public PartyChangeStatus Status { get; }

    public StoredParty? Party { get; }
}
}
