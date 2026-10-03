using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A real store whose inventory commits, while <see cref="IsLosingAnswers" /> is set, happen and then fail as if the
///     answer was lost on the way back, so the server has to settle them from the ledger.
/// </summary>
internal sealed class LosingAnswersGameStore : IGameStore
{
    private readonly IGameStore m_inner;
    private int m_lostAnswers;

    public LosingAnswersGameStore(IGameStore inner)
    {
        m_inner = inner;
    }

    public bool IsLosingAnswers { get; set; }

    /// <summary>
    ///     How many commits happened whose answer was lost.
    /// </summary>
    public int LostAnswers => Volatile.Read(ref m_lostAnswers);

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        return m_inner.GetPendingMigrationsAsync(cancellationToken);
    }

    public Task<AccountId?> ProvisionAccountAsync(
        string loginNormalized,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return m_inner.ProvisionAccountAsync(loginNormalized, now, cancellationToken);
    }

    public Task<AccountId?> CreateAccountAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return m_inner.CreateAccountAsync(loginNormalized, passwordScheme, passwordHash, now, cancellationToken);
    }

    public Task<AccountId?> SetAccountPasswordAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        return m_inner.SetAccountPasswordAsync(loginNormalized, passwordScheme, passwordHash, cancellationToken);
    }

    public Task<AccountCredentials?> FindAccountCredentialsAsync(
        string loginNormalized,
        CancellationToken cancellationToken)
    {
        return m_inner.FindAccountCredentialsAsync(loginNormalized, cancellationToken);
    }

    public Task IssueSessionTokenAsync(
        AccountId account,
        byte[] tokenHash,
        DateTime issuedAt,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        return m_inner.IssueSessionTokenAsync(account, tokenHash, issuedAt, expiresAt, cancellationToken);
    }

    public Task<StoredSessionToken?> FindSessionTokenAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        return m_inner.FindSessionTokenAsync(tokenHash, cancellationToken);
    }

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(
        AccountId account,
        CancellationToken cancellationToken)
    {
        return m_inner.ListCharactersAsync(account, cancellationToken);
    }

    public Task<CharacterCreation> CreateCharacterAsync(
        AccountId account,
        NewCharacter character,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        return m_inner.CreateCharacterAsync(account, character, maxCharacters, cancellationToken);
    }

    public Task<StoredCharacter?> LoadCharacterAsync(
        AccountId account,
        long characterId,
        CancellationToken cancellationToken)
    {
        return m_inner.LoadCharacterAsync(account, characterId, cancellationToken);
    }

    public Task<StoredParty?> LoadPartyAsync(long characterId, CancellationToken cancellationToken)
    {
        return m_inner.LoadPartyAsync(characterId, cancellationToken);
    }

    public Task<PartyChangeResult> CommitPartyChangeAsync(PartyChange change, CancellationToken cancellationToken)
    {
        return m_inner.CommitPartyChangeAsync(change, cancellationToken);
    }

    public Task<StoredParty?> FindPartyStateAsync(long characterId, CancellationToken cancellationToken)
    {
        return m_inner.FindPartyStateAsync(characterId, cancellationToken);
    }

    public Task SaveCheckpointAsync(CharacterCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        return m_inner.SaveCheckpointAsync(checkpoint, cancellationToken);
    }

    public Task<InventoryResult> CommitPickupAsync(PickupCommit pickup, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitPickupAsync(pickup, cancellationToken));
    }

    public Task<InventoryResult> CommitEquipAsync(EquipCommit equip, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitEquipAsync(equip, cancellationToken));
    }

    public Task<InventoryResult> CommitUnequipAsync(UnequipCommit unequip, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitUnequipAsync(unequip, cancellationToken));
    }

    public Task<InventoryResult> CommitConsumeAsync(ConsumeCommit consume, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitConsumeAsync(consume, cancellationToken));
    }

    public Task<InventoryResult> CommitBuyAsync(BuyCommit buy, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitBuyAsync(buy, cancellationToken));
    }

    public Task<InventoryResult> CommitSellAsync(SellCommit sell, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitSellAsync(sell, cancellationToken));
    }

    public Task<InventoryResult> CommitGrantAsync(GrantCommit grant, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitGrantAsync(grant, cancellationToken));
    }

    public Task<InventoryResult> CommitQuestRewardAsync(QuestRewardCommit reward, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitQuestRewardAsync(reward, cancellationToken));
    }

    public Task<InventoryResult> CommitJobChangeAsync(JobChangeCommit change, CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitJobChangeAsync(change, cancellationToken));
    }

    public Task<InventoryResult?> FindJobChangeAsync(
        Guid operationId,
        long characterId,
        string job,
        CancellationToken cancellationToken)
    {
        return m_inner.FindJobChangeAsync(operationId, characterId, job, cancellationToken);
    }

    public Task<InventoryResult?> FindOperationAsync(
        Guid operationId,
        long characterId,
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken)
    {
        return m_inner.FindOperationAsync(operationId, characterId, rowIds, cancellationToken);
    }

    public async Task<TradeResult> CommitTradeAsync(TradeCommit trade, CancellationToken cancellationToken)
    {
        TradeResult result = await m_inner.CommitTradeAsync(trade, cancellationToken).ConfigureAwait(false);
        if (IsLosingAnswers && result.Status == TradeStatus.Committed)
        {
            Interlocked.Increment(ref m_lostAnswers);
            throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
        }

        return result;
    }

    public Task<TradeResult?> FindTradeAsync(
        Guid tradeId,
        long firstCharacterId,
        long secondCharacterId,
        CancellationToken cancellationToken)
    {
        return m_inner.FindTradeAsync(tradeId, firstCharacterId, secondCharacterId, cancellationToken);
    }

    public Task<StoredStorage> ReadStorageAsync(AccountId account, CancellationToken cancellationToken)
    {
        return m_inner.ReadStorageAsync(account, cancellationToken);
    }

    public Task<StorageResult> CommitStorageDepositAsync(
        StorageDepositCommit deposit,
        CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitStorageDepositAsync(deposit, cancellationToken));
    }

    public Task<StorageResult> CommitStorageWithdrawAsync(
        StorageWithdrawCommit withdraw,
        CancellationToken cancellationToken)
    {
        return AnswerAsync(m_inner.CommitStorageWithdrawAsync(withdraw, cancellationToken));
    }

    public Task<StorageResult?> FindStorageOperationAsync(
        Guid operationId,
        long characterId,
        CancellationToken cancellationToken)
    {
        return m_inner.FindStorageOperationAsync(operationId, characterId, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListStoredDefinitionIdsAsync(CancellationToken cancellationToken)
    {
        return m_inner.ListStoredDefinitionIdsAsync(cancellationToken);
    }

    private async Task<StorageResult> AnswerAsync(Task<StorageResult> commit)
    {
        StorageResult result = await commit.ConfigureAwait(false);
        if (IsLosingAnswers && result.Status == InventoryStatus.Committed)
        {
            Interlocked.Increment(ref m_lostAnswers);
            throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
        }

        return result;
    }

    private async Task<InventoryResult> AnswerAsync(Task<InventoryResult> commit)
    {
        InventoryResult result = await commit.ConfigureAwait(false);
        if (IsLosingAnswers && result.Status == InventoryStatus.Committed)
        {
            Interlocked.Increment(ref m_lostAnswers);
            throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
        }

        return result;
    }
}
}
