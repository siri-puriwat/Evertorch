using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One of a character's listings as stored: its item, the units listed and left, the gross coins of its sales, and
///     when it was listed and expires.
/// </summary>
public sealed class StoredMarketListing
{
    public StoredMarketListing(
        long id,
        string itemDefinitionId,
        int unitsListed,
        int unitsLeft,
        long grossCoins,
        DateTime listedAt,
        DateTime expiresAt)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId;
        UnitsListed = unitsListed;
        UnitsLeft = unitsLeft;
        GrossCoins = grossCoins;
        ListedAt = listedAt;
        ExpiresAt = expiresAt;
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }

    public int UnitsListed { get; }

    public int UnitsLeft { get; }

    public long GrossCoins { get; }

    public DateTime ListedAt { get; }

    public DateTime ExpiresAt { get; }
}
}
