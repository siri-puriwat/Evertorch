using System.Collections.Generic;
using System.Text.Json;

namespace Evertorch.Tools
{
/// <summary>
/// Presentation and player-visible lookup data only. Fields are copied one by one from an allow-list;
/// a new model field reaches the client only when someone adds it here on purpose.
/// </summary>
internal static class ClientProjection
{
    public static IReadOnlyList<PackageFile> Write(ContentSet content)
    {
        return new List<PackageFile>
        {
            new PackageFile("items.json", PackageJson.Write(writer => WriteItems(writer, content))),
            new PackageFile("jobs.json", PackageJson.Write(writer => WriteJobs(writer, content))),
            new PackageFile("maps.json", PackageJson.Write(writer => WriteMaps(writer, content))),
            new PackageFile("monsters.json", PackageJson.Write(writer => WriteMonsters(writer, content))),
            new PackageFile("skills.json", PackageJson.Write(writer => WriteSkills(writer, content))),
        };
    }

    private static void WriteItems(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (ItemDefinition item in content.Items)
        {
            writer.WriteStartObject();
            writer.WriteString("id", item.Id.Value);
            writer.WriteString("displayName", item.DisplayName);
            writer.WriteString("type", EnumText.Of(item.Type));
            writer.WriteNumber("stackLimit", item.StackLimit);
            writer.WriteString("icon", item.Icon);
            writer.WriteString("model", item.Model);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteJobs(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (JobDefinition job in content.Jobs)
        {
            writer.WriteStartObject();
            writer.WriteString("id", job.Id.Value);
            writer.WriteString("displayName", job.DisplayName);
            writer.WriteString("prefab", job.Prefab);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteMaps(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (MapDefinition map in content.Maps)
        {
            writer.WriteStartObject();
            writer.WriteString("id", map.Id.Value);
            writer.WriteString("displayName", map.DisplayName);
            writer.WriteString("scene", map.Scene);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteMonsters(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (MonsterDefinition monster in content.Monsters)
        {
            writer.WriteStartObject();
            writer.WriteString("id", monster.Id.Value);
            writer.WriteString("displayName", monster.DisplayName);
            writer.WriteNumber("level", monster.Level);
            writer.WriteString("prefab", monster.Prefab);
            writer.WriteString("icon", monster.Icon);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteSkills(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (SkillDefinition skill in content.Skills)
        {
            writer.WriteStartObject();
            writer.WriteString("id", skill.Id.Value);
            writer.WriteString("displayName", skill.DisplayName);
            writer.WriteString("targetType", EnumText.Of(skill.TargetType));
            writer.WriteString("icon", skill.Icon);
            writer.WriteEndObject();
        }

        EndFile(writer);
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
