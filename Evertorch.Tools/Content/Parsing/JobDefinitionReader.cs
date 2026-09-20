using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class JobDefinitionReader
{
    public static AuthoredJob? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        JobDefinitionId id = root.RequiredId<JobDefinitionId>(
            "id",
            JobDefinitionId.TryCreate,
            JobDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");

        YamlFieldReader server = root.RequiredMapping("server");

        YamlFieldReader stats = server.RequiredMapping("startingStats");
        int str = stats.RequiredInt("str", 0, ContentLimits.MaxStat);
        int agi = stats.RequiredInt("agi", 0, ContentLimits.MaxStat);
        int vit = stats.RequiredInt("vit", 0, ContentLimits.MaxStat);
        int intelligence = stats.RequiredInt("int", 0, ContentLimits.MaxStat);
        int dex = stats.RequiredInt("dex", 0, ContentLimits.MaxStat);
        int luk = stats.RequiredInt("luk", 0, ContentLimits.MaxStat);

        YamlFieldReader health = server.RequiredMapping("health");
        int healthBase = health.RequiredInt("base", 1, ContentLimits.MaxHp);
        int healthPerLevel = health.RequiredInt("perLevel", 0, ContentLimits.MaxHp);

        YamlFieldReader spirit = server.RequiredMapping("spirit");
        int spiritBase = spirit.RequiredInt("base", 0, ContentLimits.MaxHp);
        int spiritPerLevel = spirit.RequiredInt("perLevel", 0, ContentLimits.MaxHp);

        int unarmedAttackSpeedPenalty = server.RequiredInt("unarmedAttackSpeedPenalty", 0, 200);

        YamlFieldReader movement = server.RequiredMapping("movement");
        double baseSpeed = movement.RequiredDouble("baseSpeed", 0d, ContentLimits.MaxSpeed, true);

        MapDefinitionId startingMap = server.RequiredId<MapDefinitionId>(
            "startingMap",
            MapDefinitionId.TryCreate,
            MapDefinitionId.KindPrefix);
        SkillDefinitionId basicAttack = server.RequiredId<SkillDefinitionId>(
            "basicAttack",
            SkillDefinitionId.TryCreate,
            SkillDefinitionId.KindPrefix);

        YamlFieldReader client = root.RequiredMapping("client");
        string prefab = client.RequiredAssetKey("prefab");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        JobDefinition definition = new JobDefinition(
            id,
            displayName,
            new PrimaryStats(str, agi, vit, intelligence, dex, luk),
            healthBase,
            healthPerLevel,
            spiritBase,
            spiritPerLevel,
            unarmedAttackSpeedPenalty,
            baseSpeed,
            startingMap,
            basicAttack);
        return new AuthoredJob(root.ToSource(), definition, prefab);
    }
}
}
