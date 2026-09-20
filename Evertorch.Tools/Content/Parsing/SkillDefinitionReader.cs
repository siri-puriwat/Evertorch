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
        SkillDamageType damageType = root.RequiredEnum<SkillDamageType>("damageType");

        YamlFieldReader server = root.RequiredMapping("server");
        double range = server.RequiredDouble("range", 0d, ContentLimits.MaxDistance, false);

        YamlFieldReader client = root.RequiredMapping("client");
        string icon = client.RequiredAssetKey("icon");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        SkillDefinition definition = new SkillDefinition(id, displayName, targetType, damageType, range);
        return new AuthoredSkill(root.ToSource(), definition, icon);
    }
}
}
