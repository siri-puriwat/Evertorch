using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Evertorch.Persistence
{
/// <summary>
///     A row's cards as its instance data holds them (Persistence §4): null for none, else <c>{"cards": [...]}</c>, one
///     to <see cref="MaxCards" /> item IDs in slot order. The database's check keeps every row to that shape, so the
///     reader refuses anything else as a corrupt row.
/// </summary>
internal static class CardsJson
{
    /// <summary>
    ///     The most cards a row holds, a weapon's four slots (Content Pipeline §4).
    /// </summary>
    public const int MaxCards = 4;

    private const string CardsKey = "cards";

    public static IReadOnlyList<string> Read(string? json)
    {
        if (json == null)
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Corrupt(json);
        }

        var cards = new List<string>();
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name != CardsKey || property.Value.ValueKind != JsonValueKind.Array || cards.Count > 0)
            {
                throw Corrupt(json);
            }

            foreach (JsonElement card in property.Value.EnumerateArray())
            {
                if (card.ValueKind != JsonValueKind.String)
                {
                    throw Corrupt(json);
                }

                cards.Add(card.GetString()!);
            }
        }

        if (cards.Count == 0 || cards.Count > MaxCards)
        {
            throw Corrupt(json);
        }

        return cards;
    }

    public static string? Write(IReadOnlyList<string> cards)
    {
        if (cards.Count > MaxCards)
        {
            throw new ArgumentException($"A row holds at most {MaxCards} cards.", nameof(cards));
        }

        return cards.Count == 0
            ? null
            : JsonSerializer.Serialize(new Dictionary<string, IReadOnlyList<string>>
            {
                [CardsKey] = cards
            });
    }

    private static InvalidOperationException Corrupt(string json)
    {
        return new InvalidOperationException($"A row's instance data is not a list of cards: {json}");
    }
}
}
