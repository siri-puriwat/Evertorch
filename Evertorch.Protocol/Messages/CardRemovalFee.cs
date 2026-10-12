using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One card's fee to take it out at the Smith (Gameplay Systems §11.7).
/// </summary>
public readonly struct CardRemovalFee
{
    public CardRemovalFee(ItemDefinitionId card, uint fee)
    {
        if (card == default)
        {
            throw new ArgumentException("A card has an item ID.", nameof(card));
        }

        if (fee > ContentLimits.MaxCardRemovalFee)
        {
            throw new ArgumentOutOfRangeException(nameof(fee), fee, "A fee is at most 1,000,000.");
        }

        Card = card;
        Fee = fee;
    }

    public ItemDefinitionId Card { get; }

    public uint Fee { get; }
}
}
