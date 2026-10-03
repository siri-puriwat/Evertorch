namespace Evertorch.Persistence
{
/// <summary>
///     An account's storage, made by its first deposit (Persistence §4).
/// </summary>
internal sealed class AccountStorageRow
{
    public long AccountId { get; set; }

    public long Revision { get; set; }

    public int Version { get; set; }
}
}
