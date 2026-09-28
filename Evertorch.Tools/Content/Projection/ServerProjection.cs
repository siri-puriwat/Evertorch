using System.Collections.Generic;
using System.Text.Json;
using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     Everything the authoritative simulation needs, including values the client must never receive.
/// </summary>
internal static class ServerProjection
{
    public static IReadOnlyList<PackageFile> Write(ContentSet content)
    {
        return new List<PackageFile>
        {
            new("experience.json", PackageJson.Write(writer => WriteExperienceTables(writer, content))),
            new("items.json", PackageJson.Write(writer => WriteItems(writer, content))),
            new("jobs.json", PackageJson.Write(writer => WriteJobs(writer, content))),
            new("maps.json", PackageJson.Write(writer => WriteMaps(writer, content))),
            new("monsters.json", PackageJson.Write(writer => WriteMonsters(writer, content))),
            new("npcs.json", PackageJson.Write(writer => WriteNpcs(writer, content))),
            new("quests.json", PackageJson.Write(writer => WriteQuests(writer, content))),
            new("skills.json", PackageJson.Write(writer => WriteSkills(writer, content))),
            new("status-effects.json", PackageJson.Write(writer => WriteStatusEffects(writer, content)))
        };
    }

    private static void WriteExperienceTables(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredExperienceTable authored in content.ExperienceTables)
        {
            ExperienceTableDefinition table = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", table.Id.Value);
            writer.WriteStartArray("levels");
            foreach (int level in table.Levels)
            {
                writer.WriteNumberValue(level);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteItems(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredItem authored in content.Items)
        {
            ItemDefinition item = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", item.Id.Value);
            writer.WriteString("displayName", item.DisplayName);
            writer.WriteString("type", EnumText.Of(item.Type));
            writer.WriteNumber("stackLimit", item.StackLimit);
            writer.WriteNumber("weight", item.Weight);
            writer.WriteNumber("sellPrice", item.SellPrice);
            if (item.Equipment != null)
            {
                ItemEquipment equipment = item.Equipment;
                writer.WriteStartObject("equipment");
                writer.WriteNumber("attack", equipment.Attack);
                writer.WriteNumber("attackSpeedPenalty", equipment.AttackSpeedPenalty);
                writer.WriteNumber("defense", equipment.Defense);
                writer.WriteStartObject("bonus");
                writer.WriteNumber("str", equipment.Bonus.Str);
                writer.WriteNumber("agi", equipment.Bonus.Agi);
                writer.WriteNumber("vit", equipment.Bonus.Vit);
                writer.WriteNumber("int", equipment.Bonus.Int);
                writer.WriteNumber("dex", equipment.Bonus.Dex);
                writer.WriteNumber("luk", equipment.Bonus.Luk);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            if (item.Effect != null)
            {
                writer.WriteStartObject("effect");
                writer.WriteNumber("hp", item.Effect.Health);
                writer.WriteNumber("sp", item.Effect.Spirit);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteJobs(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredJob authored in content.Jobs)
        {
            JobDefinition job = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", job.Id.Value);
            writer.WriteString("displayName", job.DisplayName);
            writer.WriteStartObject("startingStats");
            writer.WriteNumber("str", job.StartingStats.Str);
            writer.WriteNumber("agi", job.StartingStats.Agi);
            writer.WriteNumber("vit", job.StartingStats.Vit);
            writer.WriteNumber("int", job.StartingStats.Int);
            writer.WriteNumber("dex", job.StartingStats.Dex);
            writer.WriteNumber("luk", job.StartingStats.Luk);
            writer.WriteEndObject();
            writer.WriteNumber("healthBase", job.HealthBase);
            writer.WriteNumber("healthPerLevel", job.HealthPerLevel);
            writer.WriteNumber("spiritBase", job.SpiritBase);
            writer.WriteNumber("spiritPerLevel", job.SpiritPerLevel);
            writer.WriteNumber("unarmedAttackSpeedPenalty", job.UnarmedAttackSpeedPenalty);
            writer.WriteNumber("baseSpeed", job.BaseSpeed);
            writer.WriteString("startingMap", job.StartingMap.Value);
            writer.WriteString("basicAttack", job.BasicAttack.Value);
            writer.WriteString("experienceTable", job.ExperienceTable.Value);
            writer.WriteString("jobExperienceTable", job.JobExperienceTable.Value);
            writer.WriteStartArray("skills");
            foreach (SkillDefinitionId skill in job.Skills)
            {
                writer.WriteStringValue(skill.Value);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteMaps(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredMap authored in content.Maps)
        {
            MapDefinition map = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", map.Id.Value);
            writer.WriteString("displayName", map.DisplayName);
            writer.WriteStartObject("spawnPoint");
            WritePosition(writer, "position", map.SpawnPosition);
            writer.WriteStartObject("facing");
            writer.WriteNumber("x", map.SpawnFacing.X);
            writer.WriteNumber("z", map.SpawnFacing.Z);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartArray("monsterSpawns");
            foreach (MonsterSpawn spawn in map.MonsterSpawns)
            {
                writer.WriteStartObject();
                writer.WriteString("monster", spawn.Monster.Value);
                WritePosition(writer, "center", spawn.Center);
                writer.WriteNumber("radius", spawn.Radius);
                writer.WriteNumber("count", spawn.Count);
                writer.WriteNumber("respawnMs", spawn.RespawnMs);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("portals");
            foreach (MapPortal portal in map.Portals)
            {
                writer.WriteStartObject();
                WritePosition(writer, "center", portal.Center);
                writer.WriteNumber("radius", portal.Radius);
                writer.WriteStartObject("destination");
                writer.WriteString("map", portal.DestinationMap.Value);
                WritePosition(writer, "position", portal.DestinationPosition);
                writer.WriteStartObject("facing");
                writer.WriteNumber("x", portal.DestinationFacing.X);
                writer.WriteNumber("z", portal.DestinationFacing.Z);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("npcs");
            foreach (NpcPlacement npc in map.Npcs)
            {
                writer.WriteStartObject();
                writer.WriteString("npc", npc.Npc.Value);
                WritePosition(writer, "position", npc.Position);
                writer.WriteStartObject("facing");
                writer.WriteNumber("x", npc.Facing.X);
                writer.WriteNumber("z", npc.Facing.Z);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            NavigationJson.Write(writer, map.Navigation);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteNpcs(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredNpc authored in content.Npcs)
        {
            NpcDefinition npc = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", npc.Id.Value);
            writer.WriteString("displayName", npc.DisplayName);
            writer.WriteStartArray("shop");
            foreach (ShopEntry entry in npc.Shop)
            {
                writer.WriteStartObject();
                writer.WriteString("item", entry.Item.Value);
                writer.WriteNumber("price", entry.Price);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteBoolean("reset", npc.OffersReset);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteQuests(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredQuest authored in content.Quests)
        {
            QuestDefinition quest = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", quest.Id.Value);
            writer.WriteString("displayName", quest.DisplayName);
            writer.WriteString("giver", quest.Giver.Value);
            writer.WriteString("monster", quest.Monster.Value);
            writer.WriteNumber("count", quest.Count);
            writer.WriteNumber("baseExperience", quest.BaseExperience);
            writer.WriteNumber("jobExperience", quest.JobExperience);
            writer.WriteNumber("currency", quest.Currency);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteMonsters(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredMonster authored in content.Monsters)
        {
            MonsterDefinition monster = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", monster.Id.Value);
            writer.WriteString("displayName", monster.DisplayName);
            writer.WriteNumber("level", monster.Level);
            writer.WriteNumber("hp", monster.Hp);
            writer.WriteNumber("physicalAttack", monster.PhysicalAttack);
            writer.WriteNumber("physicalDefense", monster.PhysicalDefense);
            writer.WriteNumber("hit", monster.Hit);
            writer.WriteNumber("flee", monster.Flee);
            writer.WriteNumber("magicAttack", monster.MagicAttack);
            writer.WriteNumber("baseSpeed", monster.BaseSpeed);
            writer.WriteNumber("attackRange", monster.AttackRange);
            writer.WriteNumber("attackIntervalMs", monster.AttackIntervalMs);
            writer.WriteString("behavior", EnumText.Of(monster.Behavior));
            writer.WriteNumber("perceptionRadius", monster.PerceptionRadius);
            writer.WriteNumber("leashRadius", monster.LeashRadius);
            writer.WriteNumber("roamRadius", monster.RoamRadius);
            writer.WriteNumber("idlePauseMinMs", monster.IdlePauseMinMs);
            writer.WriteNumber("idlePauseMaxMs", monster.IdlePauseMaxMs);
            writer.WriteNumber("scanIntervalMs", monster.ScanIntervalMs);
            writer.WriteNumber("keepDistance", monster.KeepDistance);
            writer.WriteNumber("baseExperience", monster.BaseExperience);
            writer.WriteNumber("jobExperience", monster.JobExperience);
            writer.WriteStartArray("drops");
            foreach (MonsterDrop drop in monster.Drops)
            {
                writer.WriteStartObject();
                writer.WriteString("item", drop.Item.Value);
                writer.WriteNumber("chance", drop.Chance);
                writer.WriteNumber("minAmount", drop.MinAmount);
                writer.WriteNumber("maxAmount", drop.MaxAmount);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("skills");
            foreach (MonsterSkill skill in monster.Skills)
            {
                writer.WriteStartObject();
                writer.WriteString("skill", skill.Skill.Value);
                writer.WriteNumber("chance", skill.Chance);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteSkills(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredSkill authored in content.Skills)
        {
            SkillDefinition skill = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", skill.Id.Value);
            writer.WriteString("displayName", skill.DisplayName);
            writer.WriteString("targetType", EnumText.Of(skill.TargetType));
            if (skill.DamageType.HasValue)
            {
                writer.WriteString("damageType", EnumText.Of(skill.DamageType.Value));
            }

            writer.WriteNumber("range", skill.Range);
            writer.WriteNumber("spCost", skill.SpCost);
            writer.WriteString("spPaidAt", EnumText.Of(skill.SpPaidAt));
            writer.WriteNumber("fixedCastMs", skill.FixedCastMs);
            writer.WriteNumber("variableCastMs", skill.VariableCastMs);
            writer.WriteNumber("afterCastDelayMs", skill.AfterCastDelayMs);
            writer.WriteNumber("cooldownMs", skill.CooldownMs);
            if (skill.Effect != null)
            {
                writer.WriteStartObject("effect");
                if (skill.Effect.Kind == SkillEffectKind.Damage)
                {
                    writer.WriteNumber("damageRatio", skill.Effect.DamageRatioPercent);
                }
                else if (skill.Effect.Kind == SkillEffectKind.Status)
                {
                    writer.WriteString("status", skill.Effect.Status.Value);
                    writer.WriteNumber("durationMs", skill.Effect.StatusDurationMs);
                }
                else
                {
                    writer.WriteNumber("healHp", skill.Effect.HealHp);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteStatusEffects(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredStatusEffect authored in content.StatusEffects)
        {
            StatusEffectDefinition status = authored.Definition;
            writer.WriteStartObject();
            writer.WriteString("id", status.Id.Value);
            writer.WriteString("displayName", status.DisplayName);
            writer.WriteStartObject("statPercent");
            writer.WriteNumber("str", status.StatPercent.Str);
            writer.WriteNumber("agi", status.StatPercent.Agi);
            writer.WriteNumber("vit", status.StatPercent.Vit);
            writer.WriteNumber("int", status.StatPercent.Int);
            writer.WriteNumber("dex", status.StatPercent.Dex);
            writer.WriteNumber("luk", status.StatPercent.Luk);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WritePosition(Utf8JsonWriter writer, string name, WorldPosition position)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("x", position.X);
        writer.WriteNumber("y", position.Y);
        writer.WriteNumber("z", position.Z);
        writer.WriteEndObject();
    }

    private static void BeginFile(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", ContentPackageBuilder.SchemaVersion);
        writer.WriteStartArray("definitions");
    }

    private static void EndFile(Utf8JsonWriter writer)
    {
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
}
