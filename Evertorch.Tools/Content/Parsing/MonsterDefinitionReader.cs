using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class MonsterDefinitionReader
{
    public static AuthoredMonster? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        MonsterDefinitionId id = root.RequiredId<MonsterDefinitionId>(
            "id",
            MonsterDefinitionId.TryCreate,
            MonsterDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");
        int level = root.RequiredInt("level", 1, ContentLimits.MaxLevel);

        YamlFieldReader stats = root.RequiredMapping("stats");
        int hp = stats.RequiredInt("hp", 1, ContentLimits.MaxHp);
        int physicalAttack = stats.RequiredInt("physicalAttack", 0, ContentLimits.MaxStat);
        int physicalDefense = stats.RequiredInt("physicalDefense", 0, ContentLimits.MaxStat);
        int hit = stats.RequiredInt("hit", 0, ContentLimits.MaxStat);
        int flee = stats.RequiredInt("flee", 0, ContentLimits.MaxStat);

        YamlFieldReader movement = root.RequiredMapping("movement");
        double baseSpeed = movement.RequiredDouble("baseSpeed", 0d, ContentLimits.MaxSpeed, false);

        YamlFieldReader combat = root.RequiredMapping("combat");
        double attackRange = combat.RequiredDouble("attackRange", 0d, ContentLimits.MaxDistance, true);
        int attackIntervalMs = combat.RequiredInt("attackIntervalMs", 1, ContentLimits.MaxDurationMs);

        YamlFieldReader ai = root.RequiredMapping("ai");
        MonsterBehavior behavior = ai.RequiredEnum<MonsterBehavior>("behavior");
        double perceptionRadius = ai.RequiredDouble("perceptionRadius", 0d, ContentLimits.MaxDistance, false);
        double leashRadius = ai.RequiredDouble("leashRadius", 0d, ContentLimits.MaxDistance, true);
        double roamRadius = ai.RequiredDouble("roamRadius", 0d, ContentLimits.MaxDistance, false);
        YamlFieldReader idlePause = ai.RequiredMapping("idlePauseMs");
        int idlePauseMinMs = idlePause.RequiredInt("min", 0, ContentLimits.MaxDurationMs);
        int idlePauseMaxMs = idlePause.RequiredInt("max", 0, ContentLimits.MaxDurationMs);
        if (idlePauseMinMs > idlePauseMaxMs)
        {
            idlePause.ReportField("min", "must not be greater than max");
        }

        int scanIntervalMs = ai.RequiredInt("scanIntervalMs", 1, ContentLimits.MaxDurationMs);

        var drops = new List<MonsterDrop>();
        foreach (YamlFieldReader drop in root.OptionalMappingSequence("drops"))
        {
            drops.Add(ReadDrop(drop));
        }

        YamlFieldReader client = root.RequiredMapping("client");
        string prefab = client.RequiredAssetKey("prefab");
        string icon = client.RequiredAssetKey("icon");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new MonsterDefinition(
            id,
            displayName,
            level,
            hp,
            physicalAttack,
            physicalDefense,
            hit,
            flee,
            baseSpeed,
            attackRange,
            attackIntervalMs,
            behavior,
            perceptionRadius,
            leashRadius,
            roamRadius,
            idlePauseMinMs,
            idlePauseMaxMs,
            scanIntervalMs,
            drops);
        return new AuthoredMonster(root.ToSource(), definition, prefab, icon);
    }

    private static MonsterDrop ReadDrop(YamlFieldReader drop)
    {
        ItemDefinitionId item = drop.RequiredId<ItemDefinitionId>(
            "item",
            ItemDefinitionId.TryCreate,
            ItemDefinitionId.KindPrefix);
        double chance = drop.RequiredDouble("chance", 0d, 1d, false);

        YamlFieldReader amount = drop.RequiredMapping("amount");
        int min = amount.RequiredInt("min", 1, ContentLimits.MaxStack);
        int max = amount.RequiredInt("max", 1, ContentLimits.MaxStack);
        if (min > max)
        {
            amount.ReportField("min", "must not be greater than max");
        }

        return new MonsterDrop(item, chance, min, max);
    }
}
}
