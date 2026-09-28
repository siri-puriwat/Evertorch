using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class SkillDefinitionReader
{
    private const int MaxDescriptionLength = 240;

    public static AuthoredSkill? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        SkillDefinitionId id = root.RequiredId<SkillDefinitionId>(
            "id",
            SkillDefinitionId.TryCreate,
            SkillDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");
        SkillTargetType targetType = root.RequiredEnum<SkillTargetType>("targetType");
        SkillDamageType? damageType = root.Has("damageType")
            ? root.RequiredEnum<SkillDamageType>("damageType")
            : null;

        YamlFieldReader server = root.RequiredMapping("server");
        double range = server.RequiredDouble("range", 0d, ContentLimits.MaxDistance, false);
        SkillPaymentPoint spPaidAt = server.Has("spPaidAt")
            ? server.RequiredEnum<SkillPaymentPoint>("spPaidAt")
            : SkillPaymentPoint.Resolution;
        int maxLevel = server.Has("maxLevel") ? server.RequiredInt("maxLevel", 1, ContentLimits.MaxSkillLevel) : 1;
        List<SkillLevel> levels = ReadLevels(server, maxLevel, diagnostics);
        SkillRequirement? requires =
            server.Has("requires") ? ReadRequirement(server.RequiredMapping("requires")) : null;

        SkillEffectKind? kind = levels.Count > 0 ? levels[0].Effect.Kind : null;
        if (levels.Any(level => level.Effect.Kind != kind))
        {
            server.ReportField("levels", "every level must have the same kind of effect");
        }

        if (kind == SkillEffectKind.Damage && damageType == null)
        {
            root.ReportField("damageType", "is required for a damage effect");
        }

        // A status effect is only ever the caster's own (Gameplay Systems §9.1).
        if (kind == SkillEffectKind.Status && targetType != SkillTargetType.Self)
        {
            root.ReportField("targetType", "must be self for a status effect");
        }

        // Damage lands on an enemy; the server has no hit of a player on itself to resolve.
        if (kind == SkillEffectKind.Damage && targetType != SkillTargetType.Enemy)
        {
            root.ReportField("targetType", "must be enemy for a damage effect");
        }

        YamlFieldReader client = root.RequiredMapping("client");
        string icon = client.RequiredAssetKey("icon");
        string? description = client.Has("description") ? ReadDescription(client) : null;

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new SkillDefinition(
            id,
            displayName,
            targetType,
            damageType,
            range,
            spPaidAt,
            levels.AsReadOnly(),
            requires);
        return new AuthoredSkill(root.ToSource(), definition, icon, description);
    }

    // A skill without levels is a basic attack, which has no effect; one with levels lists exactly its maxLevel
    // (Content Pipeline §4).
    private static List<SkillLevel> ReadLevels(YamlFieldReader server, int maxLevel,
        List<ContentDiagnostic> diagnostics)
    {
        var levels = new List<SkillLevel>();
        if (!server.Has("levels"))
        {
            if (server.Has("maxLevel"))
            {
                server.ReportField("levels", "is required with maxLevel");
            }

            return levels;
        }

        int errorsBefore = diagnostics.Count;
        IReadOnlyList<YamlFieldReader> entries = server.RequiredMappingSequence("levels");
        foreach (YamlFieldReader entry in entries)
        {
            SkillLevel? level = ReadLevel(entry);
            if (level != null)
            {
                levels.Add(level);
            }
        }

        if (diagnostics.Count == errorsBefore && maxLevel > 0 && entries.Count != maxLevel)
        {
            server.ReportField(
                "levels",
                string.Format(CultureInfo.InvariantCulture, "must list exactly maxLevel ({0}) levels", maxLevel));
        }

        return levels;
    }

    private static SkillLevel? ReadLevel(YamlFieldReader level)
    {
        int spCost = OptionalInt(level, "spCost", ContentLimits.MaxHp);
        int fixedCastMs = 0;
        int variableCastMs = 0;
        if (level.Has("castTimeMs"))
        {
            YamlFieldReader castTime = level.RequiredMapping("castTimeMs");
            fixedCastMs = OptionalInt(castTime, "fixed", ContentLimits.MaxDurationMs);
            variableCastMs = OptionalInt(castTime, "variable", ContentLimits.MaxDurationMs);
        }

        int afterCastDelayMs = OptionalInt(level, "afterCastDelayMs", ContentLimits.MaxDurationMs);
        int cooldownMs = OptionalInt(level, "cooldownMs", ContentLimits.MaxDurationMs);
        SkillEffect? effect = ReadEffect(level.RequiredMapping("effect"));
        return effect != null
            ? new SkillLevel(spCost, fixedCastMs, variableCastMs, afterCastDelayMs, cooldownMs, effect)
            : null;
    }

    private static SkillRequirement? ReadRequirement(YamlFieldReader requires)
    {
        SkillDefinitionId skill = requires.RequiredId<SkillDefinitionId>(
            "skill",
            SkillDefinitionId.TryCreate,
            SkillDefinitionId.KindPrefix);
        int level = requires.RequiredInt("level", 1, ContentLimits.MaxSkillLevel);
        return skill != default && level > 0 ? new SkillRequirement(skill, level) : null;
    }

    // Written for the player, the first descriptive text in the client package (Content Pipeline §5).
    private static string? ReadDescription(YamlFieldReader client)
    {
        string description = client.RequiredString("description");
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

        return description;
    }

    private static int OptionalInt(YamlFieldReader reader, string key, int max)
    {
        return reader.Has(key) ? reader.RequiredInt(key, 0, max) : 0;
    }

    // Exactly one kind of effect (Content Pipeline §4); a problem is reported and the effect left out.
    private static SkillEffect? ReadEffect(YamlFieldReader effect)
    {
        bool isDamage = effect.Has("damage");
        bool isHeal = effect.Has("heal");
        bool isStatus = effect.Has("status");
        if ((isDamage ? 1 : 0) + (isHeal ? 1 : 0) + (isStatus ? 1 : 0) != 1)
        {
            effect.ReportField("damage", "an effect must have exactly one of damage, heal, or status");
            return null;
        }

        if (isDamage)
        {
            int ratio = effect.RequiredMapping("damage").RequiredInt("ratio", 1, ContentLimits.MaxDamageRatioPercent);
            return ratio > 0 ? SkillEffect.Damage(ratio) : null;
        }

        if (isStatus)
        {
            YamlFieldReader status = effect.RequiredMapping("status");
            StatusDefinitionId id = status.RequiredId<StatusDefinitionId>(
                "status",
                StatusDefinitionId.TryCreate,
                StatusDefinitionId.KindPrefix);
            int durationMs = status.RequiredInt("durationMs", 1, ContentLimits.MaxDurationMs);
            YamlFieldReader percent = status.RequiredMapping("statPercent");
            var statPercent = new StatPercentages(
                OptionalPercent(percent, "str"),
                OptionalPercent(percent, "agi"),
                OptionalPercent(percent, "vit"),
                OptionalPercent(percent, "int"),
                OptionalPercent(percent, "dex"),
                OptionalPercent(percent, "luk"));
            return id != default && durationMs > 0 ? SkillEffect.StatusEffect(id, durationMs, statPercent) : null;
        }

        int hp = effect.RequiredMapping("heal").RequiredInt("hp", 1, ContentLimits.MaxHp);
        return hp > 0 ? SkillEffect.Heal(hp) : null;
    }

    private static int OptionalPercent(YamlFieldReader reader, string key)
    {
        return reader.Has(key) ? reader.RequiredInt(key, 0, ContentLimits.MaxStatPercent) : 0;
    }
}
}
