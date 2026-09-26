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

        YamlFieldReader client = root.RequiredMapping("client");
        string icon = client.RequiredAssetKey("icon");
        string model = client.RequiredAssetKey("model");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new ItemDefinition(id, displayName, type, stackLimit, weight, sellPrice, equipment, effect);
        return new AuthoredItem(root.ToSource(), definition, icon, model);
    }

    // A weapon has an attack and an attack-speed penalty, armor a defense; either may add to primary statistics.
    private static ItemEquipment ReadEquipment(YamlFieldReader equipment, ItemType type)
    {
        bool isWeapon = type == ItemType.Weapon;
        int attack = isWeapon ? equipment.RequiredInt("attack", 0, ContentLimits.MaxStat) : 0;
        int penalty = isWeapon ? equipment.RequiredInt("attackSpeedPenalty", 0, ContentLimits.MaxStat) : 0;
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

        return new ItemEquipment(attack, penalty, defense, bonus);
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

    private static int OptionalStat(YamlFieldReader stats, string name)
    {
        return stats.Has(name) ? stats.RequiredInt(name, 0, ContentLimits.MaxStat) : 0;
    }
}
}
