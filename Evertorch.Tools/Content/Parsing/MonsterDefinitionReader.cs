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
        bool isBoss = root.Has("boss") && root.RequiredBool("boss");

        YamlFieldReader stats = root.RequiredMapping("stats");
        int hp = stats.RequiredInt("hp", 1, ContentLimits.MaxHp);
        int physicalAttack = stats.RequiredInt("physicalAttack", 0, ContentLimits.MaxStat);
        int physicalDefense = stats.RequiredInt("physicalDefense", 0, ContentLimits.MaxStat);
        int hit = stats.RequiredInt("hit", 0, ContentLimits.MaxStat);
        int flee = stats.RequiredInt("flee", 0, ContentLimits.MaxStat);
        int magicAttack = stats.Has("magicAttack") ? stats.RequiredInt("magicAttack", 0, ContentLimits.MaxStat) : 0;

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
        double keepDistance = ai.Has("keepDistance")
            ? ai.RequiredDouble("keepDistance", 0d, ContentLimits.MaxDistance, false)
            : 0d;
        if (keepDistance > 0d && keepDistance >= attackRange)
        {
            ai.ReportField("keepDistance", "must be below combat.attackRange");
        }

        bool assists = ai.Has("assist") && ai.RequiredBool("assist");
        double assistRadius = assists || ai.Has("assistRadius")
            ? ai.RequiredDouble("assistRadius", 0d, ContentLimits.MaxDistance, true)
            : 0d;
        if (!assists && ai.Has("assistRadius"))
        {
            ai.ReportField("assistRadius", "is only for a monster with assist: true");
        }

        int baseExperience = 0;
        int jobExperience = 0;
        int mvpExperience = 0;
        if (root.Has("rewards"))
        {
            YamlFieldReader rewards = root.RequiredMapping("rewards");
            baseExperience = rewards.RequiredInt("baseExperience", 0, ContentLimits.MaxExperience);
            jobExperience = rewards.Has("jobExperience")
                ? rewards.RequiredInt("jobExperience", 0, ContentLimits.MaxExperience)
                : 0;
            mvpExperience = rewards.Has("mvpExperience")
                ? rewards.RequiredInt("mvpExperience", 0, ContentLimits.MaxExperience)
                : 0;
            if (rewards.Has("mvpExperience") && !isBoss)
            {
                rewards.ReportField("mvpExperience", "is only for a boss");
            }
        }

        var mvpDrops = new List<MvpDrop>();
        foreach (YamlFieldReader drop in root.OptionalMappingSequence("mvpDrops"))
        {
            mvpDrops.Add(ReadMvpDrop(drop));
        }

        if (root.Has("mvpDrops") && !isBoss)
        {
            root.ReportField("mvpDrops", "is only for a boss");
        }

        var drops = new List<MonsterDrop>();
        foreach (YamlFieldReader drop in root.OptionalMappingSequence("drops"))
        {
            drops.Add(ReadDrop(drop));
        }

        var skills = new List<MonsterSkill>();
        var seenSkills = new HashSet<string>();
        foreach (YamlFieldReader entry in root.OptionalMappingSequence("skills"))
        {
            SkillDefinitionId skill = entry.RequiredId<SkillDefinitionId>(
                "skill",
                SkillDefinitionId.TryCreate,
                SkillDefinitionId.KindPrefix);
            double chance = entry.RequiredDouble("chance", 0d, 1d, false);
            if (skill != default && !seenSkills.Add(skill.Value))
            {
                entry.ReportField("skill", $"lists '{skill.Value}' more than once");
            }

            skills.Add(new MonsterSkill(skill, chance));
        }

        YamlFieldReader client = root.RequiredMapping("client");
        string prefab = client.RequiredAssetKey("prefab");
        string icon = client.RequiredAssetKey("icon");
        string? projectile = client.Has("projectile") ? client.RequiredAssetKey("projectile") : null;
        double? scale = client.Has("scale")
            ? client.RequiredDouble("scale", ContentLimits.MinBodyScale, ContentLimits.MaxBodyScale, false)
            : null;
        string? tint = client.Has("tint") ? client.RequiredColor("tint") : null;

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
            magicAttack,
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
            keepDistance,
            baseExperience,
            jobExperience,
            drops,
            skills,
            isBoss,
            assists,
            assistRadius,
            mvpExperience,
            mvpDrops);
        return new AuthoredMonster(root.ToSource(), definition, prefab, icon, projectile, scale, tint);
    }

    private static MvpDrop ReadMvpDrop(YamlFieldReader drop)
    {
        ItemDefinitionId item = drop.RequiredId<ItemDefinitionId>(
            "item",
            ItemDefinitionId.TryCreate,
            ItemDefinitionId.KindPrefix);
        double chance = drop.RequiredDouble("chance", 0d, 1d, false);
        int amount = drop.RequiredInt("amount", 1, ContentLimits.MaxStack);
        return new MvpDrop(item, chance, amount);
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
