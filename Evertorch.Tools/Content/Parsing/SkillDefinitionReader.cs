using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class SkillDefinitionReader
{
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

        // A status effect is only ever the caster's own in this milestone (Gameplay Systems §9.1).
        if (effect?.Kind == SkillEffectKind.Status && targetType != SkillTargetType.Self)
        {
            root.ReportField("targetType", "must be self for a status effect");
        }

        // Damage lands on an enemy; the server has no hit of a player on itself to resolve.
        if (effect?.Kind == SkillEffectKind.Damage && targetType != SkillTargetType.Enemy)
        {
            root.ReportField("targetType", "must be enemy for a damage effect");
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
            return id != default && durationMs > 0 ? SkillEffect.StatusEffect(id, durationMs) : null;
        }

        int hp = effect.RequiredMapping("heal").RequiredInt("hp", 1, ContentLimits.MaxHp);
        return hp > 0 ? SkillEffect.Heal(hp) : null;
    }
}
}
