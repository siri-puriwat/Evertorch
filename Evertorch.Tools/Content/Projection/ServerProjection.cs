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
            new("items.json", PackageJson.Write(writer => WriteItems(writer, content))),
            new("jobs.json", PackageJson.Write(writer => WriteJobs(writer, content))),
            new("maps.json", PackageJson.Write(writer => WriteMaps(writer, content))),
            new("monsters.json", PackageJson.Write(writer => WriteMonsters(writer, content))),
            new("skills.json", PackageJson.Write(writer => WriteSkills(writer, content)))
        };
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
            NavigationJson.Write(writer, map.Navigation);
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
            writer.WriteString("damageType", EnumText.Of(skill.DamageType));
            writer.WriteNumber("range", skill.Range);
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
