using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class JobDefinitionReader
{
    // One SkillList message carries at most this many (Network Protocol §9).
    private const int MaxSkills = 11;

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
        int str = stats.RequiredInt("str", 0, ContentLimits.MaxPrimaryStat);
        int agi = stats.RequiredInt("agi", 0, ContentLimits.MaxPrimaryStat);
        int vit = stats.RequiredInt("vit", 0, ContentLimits.MaxPrimaryStat);
        int intelligence = stats.RequiredInt("int", 0, ContentLimits.MaxPrimaryStat);
        int dex = stats.RequiredInt("dex", 0, ContentLimits.MaxPrimaryStat);
        int luk = stats.RequiredInt("luk", 0, ContentLimits.MaxPrimaryStat);

        YamlFieldReader health = server.RequiredMapping("health");
        int healthBase = health.RequiredInt("base", 1, ContentLimits.MaxHp);
        int healthPerLevel = health.RequiredInt("perLevel", 0, ContentLimits.MaxHp);

        YamlFieldReader spirit = server.RequiredMapping("spirit");
        int spiritBase = spirit.RequiredInt("base", 0, ContentLimits.MaxHp);
        int spiritPerLevel = spirit.RequiredInt("perLevel", 0, ContentLimits.MaxHp);

        int unarmedAttackSpeedPenalty =
            server.RequiredInt("unarmedAttackSpeedPenalty", 0, ContentLimits.MaxAttackSpeedPenalty);

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
        ExperienceDefinitionId experienceTable = server.RequiredId<ExperienceDefinitionId>(
            "experienceTable",
            ExperienceDefinitionId.TryCreate,
            ExperienceDefinitionId.KindPrefix);
        ExperienceDefinitionId jobExperienceTable = server.RequiredId<ExperienceDefinitionId>(
            "jobExperienceTable",
            ExperienceDefinitionId.TryCreate,
            ExperienceDefinitionId.KindPrefix);
        List<SkillDefinitionId> skills = ReadSkills(server);

        YamlFieldReader client = root.RequiredMapping("client");
        string prefab = client.RequiredAssetKey("prefab");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new JobDefinition(
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
            basicAttack,
            experienceTable,
            jobExperienceTable,
            skills.AsReadOnly());
        return new AuthoredJob(root.ToSource(), definition, prefab);
    }

    // Optional: a job without skills has only its basic attack.
    private static List<SkillDefinitionId> ReadSkills(YamlFieldReader server)
    {
        var skills = new List<SkillDefinitionId>();
        if (!server.Has("skills"))
        {
            return skills;
        }

        IReadOnlyList<string> values = server.RequiredStringSequence("skills");
        for (int index = 0; index < values.Count; index++)
        {
            string element = string.Format(CultureInfo.InvariantCulture, "skills[{0}]", index);
            if (!SkillDefinitionId.TryCreate(values[index], out SkillDefinitionId skill))
            {
                server.ReportField(
                    element,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "'{0}' is not a valid ID; expected '{1}.' followed by lowercase dot-separated segments",
                        values[index],
                        SkillDefinitionId.KindPrefix));
            }
            else if (skills.Contains(skill))
            {
                server.ReportField(element, $"lists '{skill}' more than once");
            }
            else
            {
                skills.Add(skill);
            }
        }

        if (values.Count > MaxSkills)
        {
            server.ReportField(
                "skills",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "lists more than the {0} skills a skill list carries",
                    MaxSkills));
        }

        return skills;
    }
}
}
