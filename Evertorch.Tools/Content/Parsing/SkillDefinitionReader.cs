using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class SkillDefinitionReader
{
    private const int MaxDamageRatioPercent = 10_000;

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
        int spCost = OptionalInt(server, "spCost", ContentLimits.MaxHp);
        SkillPaymentPoint spPaidAt = server.Has("spPaidAt")
            ? server.RequiredEnum<SkillPaymentPoint>("spPaidAt")
            : SkillPaymentPoint.Resolution;
        int fixedCastMs = 0;
        int variableCastMs = 0;
        if (server.Has("castTimeMs"))
        {
            YamlFieldReader castTime = server.RequiredMapping("castTimeMs");
            fixedCastMs = OptionalInt(castTime, "fixed", ContentLimits.MaxDurationMs);
            variableCastMs = OptionalInt(castTime, "variable", ContentLimits.MaxDurationMs);
        }

        int afterCastDelayMs = OptionalInt(server, "afterCastDelayMs", ContentLimits.MaxDurationMs);
        int cooldownMs = OptionalInt(server, "cooldownMs", ContentLimits.MaxDurationMs);
        SkillEffect? effect = server.Has("effect") ? ReadEffect(server.RequiredMapping("effect")) : null;
        if (effect?.Kind == SkillEffectKind.Damage && damageType == null)
        {
            root.ReportField("damageType", "is required for a damage effect");
        }

        YamlFieldReader client = root.RequiredMapping("client");
        string icon = client.RequiredAssetKey("icon");

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
            spCost,
            spPaidAt,
            fixedCastMs,
            variableCastMs,
            afterCastDelayMs,
            cooldownMs,
            effect);
        return new AuthoredSkill(root.ToSource(), definition, icon);
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
        if (isDamage == isHeal)
        {
            effect.ReportField("damage", "an effect must have exactly one of damage or heal");
            return null;
        }

        if (isDamage)
        {
            int ratio = effect.RequiredMapping("damage").RequiredInt("ratio", 1, MaxDamageRatioPercent);
            return ratio > 0 ? SkillEffect.Damage(ratio) : null;
        }

        int hp = effect.RequiredMapping("heal").RequiredInt("hp", 1, ContentLimits.MaxHp);
        return hp > 0 ? SkillEffect.Heal(hp) : null;
    }
}
}
