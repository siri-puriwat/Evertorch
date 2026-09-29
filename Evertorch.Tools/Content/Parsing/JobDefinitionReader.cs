using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class JobDefinitionReader
{
    // One SkillList message carries at most this many (Network Protocol §9).
    public const int MaxSkills = 11;

    // What a first job takes from its base job, so its own file omits them (Content Pipeline §4).
    private static readonly string[] InheritedFields =
    {
        "startingStats", "startingMap", "experienceTable", "basicAttack", "movement", "unarmedAttackSpeedPenalty"
    };

    public static JobDraft? Read(
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
        JobDefinitionId? baseJob = null;
        if (server.Has("baseJob"))
        {
            baseJob = server.RequiredId<JobDefinitionId>("baseJob", JobDefinitionId.TryCreate,
                JobDefinitionId.KindPrefix);
            foreach (string inherited in InheritedFields)
            {
                if (server.Has(inherited))
                {
                    server.ReportField(inherited, "is taken from the base job, so a first job omits it");
                }
            }
        }

        YamlFieldReader health = server.RequiredMapping("health");
        int healthBase = health.RequiredInt("base", 1, ContentLimits.MaxHp);
        int healthPerLevel = health.RequiredInt("perLevel", 0, ContentLimits.MaxHp);

        YamlFieldReader spirit = server.RequiredMapping("spirit");
        int spiritBase = spirit.RequiredInt("base", 0, ContentLimits.MaxHp);
        int spiritPerLevel = spirit.RequiredInt("perLevel", 0, ContentLimits.MaxHp);

        ExperienceDefinitionId jobExperienceTable = server.RequiredId<ExperienceDefinitionId>(
            "jobExperienceTable",
            ExperienceDefinitionId.TryCreate,
            ExperienceDefinitionId.KindPrefix);
        List<SkillDefinitionId> skills = ReadSkills(server);
        List<WeaponType> weapons = ReadWeapons(server);
        JobDefinition? definition = baseJob == null
            ? ReadBaseJob(
                server,
                id,
                displayName,
                healthBase,
                healthPerLevel,
                spiritBase,
                spiritPerLevel,
                jobExperienceTable,
                skills,
                weapons)
            : null;

        YamlFieldReader client = root.RequiredMapping("client");
        string prefab = client.RequiredAssetKey("prefab");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        DefinitionSource source = root.ToSource();
        return definition != null
            ? new JobDraft(new AuthoredJob(source, definition, prefab))
            : new JobDraft(
                source,
                prefab,
                id,
                displayName,
                baseJob.GetValueOrDefault(),
                healthBase,
                healthPerLevel,
                spiritBase,
                spiritPerLevel,
                jobExperienceTable,
                skills.AsReadOnly(),
                weapons.AsReadOnly());
    }

    private static JobDefinition ReadBaseJob(
        YamlFieldReader server,
        JobDefinitionId id,
        string displayName,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        ExperienceDefinitionId jobExperienceTable,
        List<SkillDefinitionId> skills,
        List<WeaponType> weapons)
    {
        YamlFieldReader stats = server.RequiredMapping("startingStats");
        int str = stats.RequiredInt("str", 0, ContentLimits.MaxPrimaryStat);
        int agi = stats.RequiredInt("agi", 0, ContentLimits.MaxPrimaryStat);
        int vit = stats.RequiredInt("vit", 0, ContentLimits.MaxPrimaryStat);
        int intelligence = stats.RequiredInt("int", 0, ContentLimits.MaxPrimaryStat);
        int dex = stats.RequiredInt("dex", 0, ContentLimits.MaxPrimaryStat);
        int luk = stats.RequiredInt("luk", 0, ContentLimits.MaxPrimaryStat);

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

        return new JobDefinition(
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
            skills.AsReadOnly(),
            weapons.AsReadOnly());
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

    // Every job wields at least one weapon type, each listed once (Gameplay Systems §11.1).
    private static List<WeaponType> ReadWeapons(YamlFieldReader server)
    {
        var weapons = new List<WeaponType>();
        IReadOnlyList<string> values = server.RequiredStringSequence("weapons");
        for (int index = 0; index < values.Count; index++)
        {
            string element = string.Format(CultureInfo.InvariantCulture, "weapons[{0}]", index);
            if (!TryParseWeapon(values[index], out WeaponType weapon))
            {
                server.ReportField(element, $"must be one of: {string.Join(", ", AllowedWeapons())}");
            }
            else if (weapons.Contains(weapon))
            {
                server.ReportField(element, $"lists '{values[index]}' more than once");
            }
            else
            {
                weapons.Add(weapon);
            }
        }

        if (values.Count == 0 && server.Has("weapons"))
        {
            server.ReportField("weapons", "must list at least one weapon type");
        }

        return weapons;
    }

    private static bool TryParseWeapon(string text, out WeaponType weapon)
    {
        foreach (WeaponType candidate in Enum.GetValues<WeaponType>())
        {
            if (string.Equals(EnumText.Of(candidate), text, StringComparison.Ordinal))
            {
                weapon = candidate;
                return true;
            }
        }

        weapon = default;
        return false;
    }

    private static IEnumerable<string> AllowedWeapons()
    {
        return Enum.GetValues<WeaponType>().Select(weapon => EnumText.Of(weapon));
    }
}
}
