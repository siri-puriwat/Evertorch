using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class QuestDefinitionReader
{
    public static AuthoredQuest? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        QuestDefinitionId id = root.RequiredId<QuestDefinitionId>(
            "id",
            QuestDefinitionId.TryCreate,
            QuestDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");
        YamlFieldReader server = root.RequiredMapping("server");
        NpcDefinitionId giver = server.RequiredId<NpcDefinitionId>(
            "giver",
            NpcDefinitionId.TryCreate,
            NpcDefinitionId.KindPrefix);
        YamlFieldReader kill = server.RequiredMapping("objective").RequiredMapping("kill");
        MonsterDefinitionId monster = kill.RequiredId<MonsterDefinitionId>(
            "monster",
            MonsterDefinitionId.TryCreate,
            MonsterDefinitionId.KindPrefix);
        int count = kill.RequiredInt("count", 1, ContentLimits.MaxKillCount);

        int errorsBeforeRewards = diagnostics.Count;
        YamlFieldReader rewards = server.RequiredMapping("rewards");
        int baseExperience = rewards.RequiredInt("baseExperience", 0, ContentLimits.MaxExperience);
        int currency = rewards.RequiredInt("currency", 0, ContentLimits.MaxCurrency);
        if (diagnostics.Count == errorsBeforeRewards && baseExperience == 0 && currency == 0)
        {
            server.ReportField("rewards", "must give base experience, coins, or both");
        }

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new QuestDefinition(id, displayName, giver, monster, count, baseExperience, currency);
        return new AuthoredQuest(root.ToSource(), definition);
    }
}
}
