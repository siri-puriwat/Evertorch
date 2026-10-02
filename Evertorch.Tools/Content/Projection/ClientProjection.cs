using System.Collections.Generic;
using System.Text.Json;
using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     Presentation and player-visible lookup data only. Fields are copied one by one from an allow-list;
///     a new model field reaches the client only when someone adds it here on purpose.
/// </summary>
internal static class ClientProjection
{
    public static IReadOnlyList<PackageFile> Write(ContentSet content)
    {
        return new List<PackageFile>
        {
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

    private static void WriteStatusEffects(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredStatusEffect authored in content.StatusEffects)
        {
            writer.WriteStartObject();
            writer.WriteString("id", authored.Definition.Id.Value);
            writer.WriteString("displayName", authored.Definition.DisplayName);
            if (authored.Icon != null)
            {
                writer.WriteString("icon", authored.Icon);
            }

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
            writer.WriteString("icon", authored.Icon);
            writer.WriteString("model", authored.Model);
            if (authored.Held != null)
            {
                writer.WriteString("held", authored.Held);
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
            writer.WriteString("prefab", authored.Prefab);
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
            writer.WriteString("scene", authored.Scene);

            // Geometry, not a secret: the client needs it to path optimistically and to predict collisions.
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
            writer.WriteString("prefab", authored.Prefab);
            writer.WriteString("icon", authored.Icon);
            if (authored.Projectile != null)
            {
                writer.WriteString("projectile", authored.Projectile);
            }

            // The body's size and colour and the boss flag are what the player sees, written only when authored.
            if (authored.Scale != null)
            {
                writer.WriteNumber("scale", authored.Scale.Value);
            }

            if (authored.Tint != null)
            {
                writer.WriteString("tint", authored.Tint);
            }

            if (monster.IsBoss)
            {
                writer.WriteBoolean("boss", true);
            }

            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    // A shop's prices and a quest's objective and rewards travel on the wire, where the player sees them (Content
    // Pipeline §5); the package holds names and the NPC's prefab only.
    private static void WriteNpcs(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredNpc authored in content.Npcs)
        {
            writer.WriteStartObject();
            writer.WriteString("id", authored.Definition.Id.Value);
            writer.WriteString("displayName", authored.Definition.DisplayName);
            writer.WriteString("prefab", authored.Prefab);
            writer.WriteEndObject();
        }

        EndFile(writer);
    }

    private static void WriteQuests(Utf8JsonWriter writer, ContentSet content)
    {
        BeginFile(writer);
        foreach (AuthoredQuest authored in content.Quests)
        {
            writer.WriteStartObject();
            writer.WriteString("id", authored.Definition.Id.Value);
            writer.WriteString("displayName", authored.Definition.DisplayName);
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

            // The player sees an area's reach as the telegraph of its cast (Content Pipeline §5).
            if (skill.HasArea)
            {
                writer.WriteNumber("area", skill.AreaRadius);
            }

            writer.WriteString("icon", authored.Icon);
            if (authored.Description != null)
            {
                writer.WriteString("description", authored.Description);
            }

            if (authored.Projectile != null)
            {
                writer.WriteString("projectile", authored.Projectile);
            }

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
