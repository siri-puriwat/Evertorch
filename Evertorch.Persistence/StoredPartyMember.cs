using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One member of a stored party: its character's name, job, and base level as last stored, and its account.
/// </summary>
public sealed class StoredPartyMember
{
    public StoredPartyMember(
        long characterId,
        string name,
        string jobDefinitionId,
        int baseLevel,
        AccountId account,
        int joinOrder)
    {
        CharacterId = characterId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        JobDefinitionId = jobDefinitionId ?? throw new ArgumentNullException(nameof(jobDefinitionId));
        BaseLevel = baseLevel;
        Account = account;
        JoinOrder = joinOrder;
    }

    public long CharacterId { get; }

    public string Name { get; }

    public string JobDefinitionId { get; }

    public int BaseLevel { get; }

    public AccountId Account { get; }

    public int JoinOrder { get; }
}
}
