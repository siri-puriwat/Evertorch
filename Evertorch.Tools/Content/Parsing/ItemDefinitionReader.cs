using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class ItemDefinitionReader
{
    private const int MaxDescriptionLength = 240;

    private static readonly Regex Prefix = new("^[A-Z][A-Za-z]{0,7}$", RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"[0-9]+(?:\.[0-9]+)?", RegexOptions.CultureInvariant);

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
        int slots = ReadSlots(root, type);
        ItemType? fits = ReadFits(root, type);

        YamlFieldReader server = root.RequiredMapping("server");
        int weight = server.RequiredInt("weight", 0, ContentLimits.MaxStack);
        int sellPrice = server.RequiredInt("sellPrice", 0, ContentLimits.MaxPrice);
        ItemEquipment? equipment = null;
        bool isEquipment = EquipmentPositions.IsEquipment(type);
        if (isEquipment)
        {
            if (stackLimit != 1)
            {
                root.ReportField("stackLimit", "must be 1 for equipment");
            }

            equipment = ReadEquipment(server.RequiredMapping("equipment"), type);
        }
        else if (server.Has("equipment"))
        {
            server.ReportField("equipment", "is only for equipment");
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

        CardBonus? bonus = null;
        int removalFee = 0;
        if (type == ItemType.Card)
        {
            bonus = ReadCard(server.RequiredMapping("card"), fits, out removalFee);
        }
        else if (server.Has("card"))
        {
            server.ReportField("card", "is only for a card");
        }

        ItemMarket? market = server.Has("market")
            ? ReadMarket(server.RequiredMapping("market"), sellPrice, stackLimit)
            : null;

        // Equipment but an accessory may name the table that refines it (Content Pipeline §4); the validator checks the
        // name.
        RefineDefinitionId? refine = null;
        if (server.Has("refine"))
        {
            if (type == ItemType.Accessory)
            {
                server.ReportField("refine", "is not for an accessory");
            }
            else if (isEquipment)
            {
                refine = server.RequiredId<RefineDefinitionId>(
                    "refine",
                    RefineDefinitionId.TryCreate,
                    RefineDefinitionId.KindPrefix);
            }
            else
            {
                server.ReportField("refine", "is only for equipment");
            }
        }

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

        string? worn = null;
        if (isEquipment && type != ItemType.Weapon)
        {
            worn = client.RequiredAssetKey("worn");
        }
        else if (client.Has("worn"))
        {
            client.ReportField("worn", "is only for equipment worn on the body");
        }

        string? prefix = null;
        string? description = null;
        if (type == ItemType.Card)
        {
            prefix = ReadPrefix(client);
            description = ReadDescription(client, bonus);
        }
        else
        {
            foreach (string key in new[] { "prefix", "description" }.Where(client.Has))
            {
                client.ReportField(key, "is only for a card");
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
            market,
            refine,
            slots,
            type == ItemType.Card ? new ItemCard(fits!.Value, bonus!, removalFee) : null);
        return new AuthoredItem(root.ToSource(), definition, icon, model, held, prefix, description, worn);
    }

    // A weapon holds up to four cards and any other piece of equipment one (Content Pipeline §4); 0 when absent.
    private static int ReadSlots(YamlFieldReader root, ItemType type)
    {
        if (!root.Has("slots"))
        {
            return 0;
        }

        if (!EquipmentPositions.IsEquipment(type))
        {
            root.ReportField("slots", "is only for equipment");
            return 0;
        }

        return root.RequiredInt(
            "slots",
            0,
            type == ItemType.Weapon ? ContentLimits.MaxWeaponSlots : ContentLimits.MaxArmorSlots);
    }

    // A card fits one category of equipment, and no other item names a fit.
    private static ItemType? ReadFits(YamlFieldReader root, ItemType type)
    {
        if (type != ItemType.Card)
        {
            if (root.Has("fits"))
            {
                root.ReportField("fits", "is only for a card");
            }

            return null;
        }

        string name = root.RequiredString("fits");
        foreach (ItemType category in Enum.GetValues(typeof(ItemType)))
        {
            if (EquipmentPositions.IsEquipment(category) && EnumText.Of(category) == name)
            {
                return category;
            }
        }

        if (name.Length > 0)
        {
            root.ReportField("fits", "must be weapon, armor, headgear, shield, garment, footwear, or accessory");
        }

        return null;
    }

    // What a card adds while worn, each value optional and 0 when absent, at least one above 0, and the fee to take it
    // out (Content Pipeline §4). The damage it adds by race, element, and size and takes off by element are maps from
    // names to percentages, and only a card that fits armor may give its wearer an element.
    private static CardBonus? ReadCard(YamlFieldReader card, ItemType? fits, out int removalFee)
    {
        var stats = new PrimaryStats(0, 0, 0, 0, 0, 0);
        if (card.Has("bonus"))
        {
            YamlFieldReader values = card.RequiredMapping("bonus");
            stats = new PrimaryStats(
                OptionalStat(values, "str"),
                OptionalStat(values, "agi"),
                OptionalStat(values, "vit"),
                OptionalStat(values, "int"),
                OptionalStat(values, "dex"),
                OptionalStat(values, "luk"));
        }

        var bonus = new CardBonus(
            stats,
            OptionalStat(card, "attack"),
            OptionalStat(card, "magicAttack"),
            OptionalStat(card, "defense"),
            OptionalStat(card, "maxHp"),
            OptionalStat(card, "maxSp"),
            card.Has("bossDamage") ? card.RequiredInt("bossDamage", 0, ContentLimits.MaxBossDamagePercent) : 0,
            card.Has("bossDamageReduction")
                ? card.RequiredInt("bossDamageReduction", 0, ContentLimits.MaxBossDamageReductionPercent)
                : 0,
            OptionalMap<Race>(card, "raceDamage", ContentLimits.MaxCardDamagePercent),
            OptionalMap<Element>(card, "elementDamage", ContentLimits.MaxCardDamagePercent),
            OptionalMap<BodySize>(card, "sizeDamage", ContentLimits.MaxCardDamagePercent),
            OptionalMap<Element>(card, "elementDamageReduction", ContentLimits.MaxCardDamageReductionPercent),
            card.Has("armorElement") ? card.RequiredEnum<Element>("armorElement") : Element.Neutral,
            card.Has("inflict") ? card.RequiredAilmentChanceMap("inflict") : null,
            card.Has("resist") ? card.RequiredAilmentMap("resist", 1, ContentLimits.MaxCardResistPercent) : null);
        if (card.Has("armorElement") && fits != ItemType.Armor)
        {
            card.ReportField("armorElement", "is only for a card that fits armor");
        }

        // A weapon card brings an ailment on its wearer's hits, and any other card may resist one (Gameplay Systems
        // §11.7).
        if (card.Has("inflict") && fits != ItemType.Weapon)
        {
            card.ReportField("inflict", "is only for a card that fits a weapon");
        }

        if (card.Has("resist") && fits == ItemType.Weapon)
        {
            card.ReportField("resist", "is not for a card that fits a weapon");
        }

        removalFee = card.RequiredInt("removalFee", 0, ContentLimits.MaxCardRemovalFee);
        if (!ValuesOf(bonus).Any(value => value > 0) && bonus.ArmorElement == Element.Neutral)
        {
            card.ReportField("bonus", "must add at least one value above 0");
            return null;
        }

        return bonus;
    }

    // Every value a card adds: its numbers, then its ailments' chances in basis points and their resistances.
    private static IEnumerable<int> ValuesOf(CardBonus bonus)
    {
        return NumbersOf(bonus).Concat(bonus.Inflict.Values).Concat(bonus.Resist.Values);
    }

    // Every number a card adds, in its fields' order, its groups' percentages after them.
    private static IEnumerable<int> NumbersOf(CardBonus bonus)
    {
        return new[]
            {
                bonus.Stats.Str, bonus.Stats.Agi, bonus.Stats.Vit, bonus.Stats.Int, bonus.Stats.Dex, bonus.Stats.Luk,
                bonus.Attack, bonus.MagicAttack, bonus.Defense, bonus.MaxHp, bonus.MaxSp, bonus.BossDamagePercent,
                bonus.BossDamageReductionPercent
            }
            .Concat(bonus.RaceDamage.Values)
            .Concat(bonus.ElementDamage.Values)
            .Concat(bonus.SizeDamage.Values)
            .Concat(bonus.ElementDamageReduction.Values);
    }

    // Every number a card's description must hold, as the player reads it: each value, an ailment's chance in percent.
    private static IEnumerable<string> StatedValuesOf(CardBonus bonus)
    {
        return NumbersOf(bonus)
            .Concat(bonus.Resist.Values)
            .Where(value => value > 0)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .Concat(bonus.Inflict.Values.Select(Percent));
    }

    private static string Percent(int basisPoints)
    {
        return (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);
    }

    // A run of digits too long for a decimal is kept as written; it states no value a card can have.
    private static string Normalized(string number)
    {
        return decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
            ? value.ToString("0.############", CultureInfo.InvariantCulture)
            : number;
    }

    // Every name a card's description must hold: each race, element, and size its groups name and the element it gives
    // its wearer. Undead is both a race and an element, so the description says which (Content Pipeline §7).
    private static IEnumerable<string[]> NamesOf(CardBonus bonus)
    {
        foreach (Race race in bonus.RaceDamage.Keys)
        {
            yield return race == Race.Undead ? new[] { "Undead", "race" } : new[] { race.ToString() };
        }

        IEnumerable<Element> elements = bonus.ElementDamage.Keys.Concat(bonus.ElementDamageReduction.Keys);
        if (bonus.ArmorElement != Element.Neutral)
        {
            elements = elements.Append(bonus.ArmorElement);
        }

        foreach (Element element in elements)
        {
            yield return element == Element.Undead ? new[] { "Undead", "element" } : new[] { element.ToString() };
        }

        foreach (BodySize size in bonus.SizeDamage.Keys)
        {
            yield return new[] { size.ToString() };
        }
    }

    // A card's ailments stand in its description by name, in any case: "a 5% chance to poison", "Stun resistance".
    private static IEnumerable<Ailment> AilmentsOf(CardBonus bonus)
    {
        return bonus.Inflict.Keys.Concat(bonus.Resist.Keys).Distinct();
    }

    private static IReadOnlyDictionary<TEnum, int>? OptionalMap<TEnum>(YamlFieldReader card, string key, int max)
        where TEnum : struct, Enum
    {
        return card.Has(key) ? card.RequiredEnumMap<TEnum>(key, 1, max) : null;
    }

    // The word an item's name takes for the card, "+7 Keen Iron Sword [2]" (Prototype Content §2).
    private static string? ReadPrefix(YamlFieldReader client)
    {
        string prefix = client.RequiredString("prefix");
        if (prefix.Length == 0)
        {
            return null;
        }

        if (!Prefix.IsMatch(prefix))
        {
            client.ReportField("prefix", "must be 1 to 8 letters, the first a capital");
            return null;
        }

        return prefix;
    }

    // Written for the player, who reads in it what the card does: every value above 0 stands in it as a number.
    private static string? ReadDescription(YamlFieldReader client, CardBonus? bonus)
    {
        string description = client.RequiredString("description");
        if (description.Length == 0)
        {
            return null;
        }

        if (description.Length > MaxDescriptionLength || description.Any(character => character < ' '))
        {
            client.ReportField(
                "description",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "must be 1 to {0} printable characters",
                    MaxDescriptionLength));
            return null;
        }

        if (bonus != null)
        {
            var stated = new HashSet<string>(Number.Matches(description).Select(match => Normalized(match.Value)));
            string[] missing = StatedValuesOf(bonus).Where(value => !stated.Contains(value)).ToArray();
            if (missing.Length > 0)
            {
                client.ReportField(
                    "description",
                    "must state every value of the card above 0, and leaves out " + string.Join(", ", missing));
                return null;
            }

            string[] unnamedAilments = AilmentsOf(bonus)
                .Select(EnumText.Of)
                .Where(name => description.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                .ToArray();
            if (unnamedAilments.Length > 0)
            {
                client.ReportField(
                    "description",
                    $"must name every ailment of the card, and leaves out {string.Join(", ", unnamedAilments)}");
                return null;
            }

            string[] unnamed = NamesOf(bonus)
                .Where(words => !words.All(word => description.Contains(word, StringComparison.Ordinal)))
                .Select(words => string.Join(" ", words))
                .ToArray();
            if (unnamed.Length > 0)
            {
                client.ReportField(
                    "description",
                    $"must name every race, element, and size of the card, and leaves out {string.Join(", ", unnamed)}");
                return null;
            }
        }

        return description;
    }

    // A weapon has an attack, an attack-speed penalty within the job's limit, and a type; armor a defense; a piece of
    // another category an optional defense, and a shield an optional penalty; any may add to primary statistics.
    private static ItemEquipment ReadEquipment(YamlFieldReader equipment, ItemType type)
    {
        bool isWeapon = type == ItemType.Weapon;
        int attack = isWeapon ? equipment.RequiredInt("attack", 0, ContentLimits.MaxStat) : 0;
        int penalty = 0;
        if (isWeapon || (type == ItemType.Shield && equipment.Has("attackSpeedPenalty")))
        {
            penalty = equipment.RequiredInt("attackSpeedPenalty", 0, ContentLimits.MaxAttackSpeedPenalty);
        }
        else if (equipment.Has("attackSpeedPenalty"))
        {
            equipment.ReportField("attackSpeedPenalty", "is only for a weapon or a shield");
        }

        WeaponType? weaponType = isWeapon ? equipment.RequiredEnum<WeaponType>("weaponType") : null;
        int defense = 0;
        if (type == ItemType.Armor || (!isWeapon && equipment.Has("defense")))
        {
            defense = equipment.RequiredInt("defense", 0, ContentLimits.MaxStat);
        }

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

    // A consumable restores HP and SP, each an optional flat amount, starts a status for a while, or ends ailments, at
    // least one of the four; the validator checks that the status exists and sets an attack element (Content Pipeline
    // §4).
    private static ItemEffect? ReadEffect(YamlFieldReader server, YamlFieldReader effect)
    {
        int health = OptionalStat(effect, "hp");
        int spirit = OptionalStat(effect, "sp");
        StatusDefinitionId? status = null;
        int durationMs = 0;
        if (effect.Has("status"))
        {
            YamlFieldReader started = effect.RequiredMapping("status");
            status = started.RequiredId<StatusDefinitionId>(
                "status",
                StatusDefinitionId.TryCreate,
                StatusDefinitionId.KindPrefix);
            durationMs = started.RequiredInt("durationMs", 1, ContentLimits.MaxDurationMs);
            if (status == default(StatusDefinitionId) || durationMs < 1)
            {
                return null;
            }
        }

        IReadOnlyList<Ailment> cures =
            effect.Has("cures") ? effect.RequiredAilmentSequence("cures") : new List<Ailment>();
        if (health == 0 && spirit == 0 && status == null && cures.Count == 0)
        {
            if (!effect.Has("cures"))
            {
                server.ReportField("effect", "must restore HP or SP, start a status, or end an ailment");
            }

            return null;
        }

        return new ItemEffect(health, spirit, status, durationMs, cures);
    }

    // A listable item's terms (Content Pipeline §4): a base, and a floor and a ceiling that default to the sell price,
    // at least 1, and ten times the base within what a stack may cost. The floor keeps a buy and a sale from gaining,
    // and the ceiling keeps a stack's price within the coin cap.
    private static ItemMarket? ReadMarket(YamlFieldReader market, int sellPrice, int stackLimit)
    {
        int basePrice = market.RequiredInt("basePrice", 1, ContentLimits.MaxPrice);
        int lowest = MarketPricing.DefaultFloor(sellPrice);
        int floor = market.Has("floor") ? market.RequiredInt("floor", 1, ContentLimits.MaxPrice) : lowest;
        int ceiling = market.Has("ceiling") ? market.RequiredInt("ceiling", 1, ContentLimits.MaxPrice) : 0;

        // A stack limit out of its range was reported where it was read, and no ceiling can be checked without one.
        if (stackLimit < 1)
        {
            return null;
        }

        int highest = ContentLimits.MaxCurrency / stackLimit;
        if (!market.Has("ceiling"))
        {
            ceiling = MarketPricing.DefaultCeiling(basePrice, stackLimit);
        }

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
