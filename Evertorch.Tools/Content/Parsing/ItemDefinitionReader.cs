using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class ItemDefinitionReader
{
    public static AuthoredItem? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        ItemDefinitionId id = root.RequiredId<ItemDefinitionId>(
            "id",
            ItemDefinitionId.TryCreate,
            ItemDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");
        ItemType type = root.RequiredEnum<ItemType>("type");
        int stackLimit = root.RequiredInt("stackLimit", 1, ContentLimits.MaxStack);

        YamlFieldReader server = root.RequiredMapping("server");
        int weight = server.RequiredInt("weight", 0, ContentLimits.MaxStack);
        int sellPrice = server.RequiredInt("sellPrice", 0, ContentLimits.MaxPrice);
        ItemEquipment? equipment = null;
        if (type == ItemType.Weapon || type == ItemType.Armor)
        {
            if (stackLimit != 1)
            {
                root.ReportField("stackLimit", "must be 1 for a weapon or armor");
            }

            equipment = ReadEquipment(server.RequiredMapping("equipment"), type);
        }
        else if (server.Has("equipment"))
        {
            server.ReportField("equipment", "is only for a weapon or armor");
        }

        ItemEffect? effect = null;
        if (type == ItemType.Consumable)
        {
            effect = ReadEffect(server, server.RequiredMapping("effect"));
        }
        else if (server.Has("effect"))
        {
            server.ReportField("effect", "is only for a consumable");
        }

        ItemMarket? market = server.Has("market")
            ? ReadMarket(server.RequiredMapping("market"), sellPrice, stackLimit)
            : null;

        YamlFieldReader client = root.RequiredMapping("client");
        string icon = client.RequiredAssetKey("icon");
        string model = client.RequiredAssetKey("model");
        string? held = null;
        if (client.Has("held"))
        {
            if (type == ItemType.Weapon)
            {
                held = client.RequiredAssetKey("held");
            }
            else
            {
                client.ReportField("held", "is only for a weapon");
            }
        }

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new ItemDefinition(
            id,
            displayName,
            type,
            stackLimit,
            weight,
            sellPrice,
            equipment,
            effect,
            market);
        return new AuthoredItem(root.ToSource(), definition, icon, model, held);
    }

    // A weapon has an attack, an attack-speed penalty within the job's limit, and a type, armor a defense; either may
    // add to primary statistics.
    private static ItemEquipment ReadEquipment(YamlFieldReader equipment, ItemType type)
    {
        bool isWeapon = type == ItemType.Weapon;
        int attack = isWeapon ? equipment.RequiredInt("attack", 0, ContentLimits.MaxStat) : 0;
        int penalty = isWeapon
            ? equipment.RequiredInt("attackSpeedPenalty", 0, ContentLimits.MaxAttackSpeedPenalty)
            : 0;
        WeaponType? weaponType = isWeapon ? equipment.RequiredEnum<WeaponType>("weaponType") : null;
        int defense = isWeapon ? 0 : equipment.RequiredInt("defense", 0, ContentLimits.MaxStat);
        var bonus = new PrimaryStats(0, 0, 0, 0, 0, 0);
        if (equipment.Has("bonus"))
        {
            YamlFieldReader stats = equipment.RequiredMapping("bonus");
            bonus = new PrimaryStats(
                OptionalStat(stats, "str"),
                OptionalStat(stats, "agi"),
                OptionalStat(stats, "vit"),
                OptionalStat(stats, "int"),
                OptionalStat(stats, "dex"),
                OptionalStat(stats, "luk"));
        }

        return new ItemEquipment(attack, penalty, defense, bonus, weaponType);
    }

    // A consumable restores HP, SP, or both, each an optional flat amount.
    private static ItemEffect? ReadEffect(YamlFieldReader server, YamlFieldReader effect)
    {
        int health = OptionalStat(effect, "hp");
        int spirit = OptionalStat(effect, "sp");
        if (health == 0 && spirit == 0)
        {
            server.ReportField("effect", "must restore HP, SP, or both");
            return null;
        }

        return new ItemEffect(health, spirit);
    }

    // A listable item's terms (Content Pipeline §4): a base, and a floor and a ceiling that default to the sell price,
    // at least 1, and ten times the base within what a stack may cost. The floor keeps a buy and a sale from gaining,
    // and the ceiling keeps a stack's price within the coin cap.
    private static ItemMarket? ReadMarket(YamlFieldReader market, int sellPrice, int stackLimit)
    {
        int highest = ContentLimits.MaxCurrency / stackLimit;
        int basePrice = market.RequiredInt("basePrice", 1, ContentLimits.MaxPrice);
        int lowest = MarketPricing.DefaultFloor(sellPrice);
        int floor = market.Has("floor") ? market.RequiredInt("floor", 1, ContentLimits.MaxPrice) : lowest;
        int ceiling = market.Has("ceiling")
            ? market.RequiredInt("ceiling", 1, ContentLimits.MaxPrice)
            : MarketPricing.DefaultCeiling(basePrice, stackLimit);
        bool isValid = true;
        if (floor < lowest)
        {
            market.ReportField("floor", $"must be at least {lowest}, the sell price and at least 1");
            isValid = false;
        }
        else if (floor > basePrice)
        {
            market.ReportField(market.Has("floor") ? "floor" : "basePrice", "the floor must not pass the base price");
            isValid = false;
        }

        if (ceiling < basePrice)
        {
            market.ReportField(market.Has("ceiling") ? "ceiling" : "basePrice",
                "the ceiling must be at least the base price");
            isValid = false;
        }
        else if (ceiling > highest)
        {
            market.ReportField(
                market.Has("ceiling") ? "ceiling" : "basePrice",
                $"a stack at the ceiling must cost at most {ContentLimits.MaxCurrency}, so at most {highest} a unit");
            isValid = false;
        }

        return isValid ? new ItemMarket(basePrice, floor, ceiling) : null;
    }

    private static int OptionalStat(YamlFieldReader stats, string name)
    {
        return stats.Has(name) ? stats.RequiredInt(name, 0, ContentLimits.MaxStat) : 0;
    }
}
}
