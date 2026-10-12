using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of the cards (Prototype Content §2): what a card fits, the free slots of a row it fits, an insertion's
///     line, and the refusals of an insertion, chosen from the client's own rows.
/// </summary>
public static class CardMessages
{
    public const string InsertPrompt = "Press again to insert";

    /// <summary>
    ///     "Fits: weapon" or "Fits: armor".
    /// </summary>
    public static string Fits(EquipmentSlot fits)
    {
        return fits switch
        {
            EquipmentSlot.Weapon => "Fits: weapon",
            EquipmentSlot.Armor => "Fits: armor",
            _ => "Fits: nothing"
        };
    }

    /// <summary>
    ///     How many free slots <paramref name="row" /> has for a card that fits <paramref name="fits" />: 0 for a worn
    ///     row, a row of another kind, or one whose slots are full.
    /// </summary>
    public static int FreeSlots(ClientContent? content, InventoryEntry row, EquipmentSlot fits)
    {
        if (row.Slot != EquipmentSlot.None
            || fits == EquipmentSlot.None
            || content == null
            || !content.TryGetItem(row.Item, out ClientItem? item)
            || item == null
            || SlotOf(item.Type) != fits)
        {
            return 0;
        }

        return item.Slots - row.Cards.Count;
    }

    /// <summary>
    ///     "Forest Crawler Card inserted: Keen Iron Sword [2]."
    /// </summary>
    public static string Inserted(ClientContent? content, ItemDefinitionId card, InventoryEntry row)
    {
        return $"{ShopMessages.ItemName(content, card)} inserted: "
            + $"{ShopMessages.RowName(content, row.Item, row.RefineLevel, row.Cards)}.";
    }

    /// <summary>
    ///     The words for a refused insertion of <paramref name="request" />, chosen from the client's own
    ///     <paramref name="rows" />; any reason without its own words reads as any refusal.
    /// </summary>
    public static string DescribeInsertRefusal(
        CardRequest request,
        IReadOnlyList<InventoryEntry> rows,
        CommandRejectionReason reason,
        ClientContent? content)
    {
        if (reason != CommandRejectionReason.NotAllowedNow ||
            !TryFind(rows, request.InventoryItem, out InventoryEntry row))
        {
            return RejectionMessages.Describe(reason, false);
        }

        string name = ShopMessages.RowName(content, row.Item, row.RefineLevel, row.Cards);
        if (row.Slot != EquipmentSlot.None)
        {
            return $"Take off {name} first.";
        }

        EquipmentSlot fits = content != null && content.TryGetItem(request.Card, out ClientItem? card) && card != null
            ? card.Fits
            : EquipmentSlot.None;
        return content != null && IsOfKind(content, row, fits)
            ? $"{name} has no free slot."
            : $"{ShopMessages.ItemName(content, request.Card)} does not fit {name}.";
    }

    private static bool IsOfKind(ClientContent content, InventoryEntry row, EquipmentSlot fits)
    {
        return content.TryGetItem(row.Item, out ClientItem? item) && item != null && SlotOf(item.Type) == fits;
    }

    private static EquipmentSlot SlotOf(ItemType type)
    {
        return type switch
        {
            ItemType.Weapon => EquipmentSlot.Weapon,
            ItemType.Armor => EquipmentSlot.Armor,
            _ => EquipmentSlot.None
        };
    }

    private static bool TryFind(IReadOnlyList<InventoryEntry> rows, long inventoryItem, out InventoryEntry row)
    {
        foreach (InventoryEntry candidate in rows)
        {
            if (candidate.InventoryItem == inventoryItem)
            {
                row = candidate;
                return true;
            }
        }

        row = default;
        return false;
    }
}
}
