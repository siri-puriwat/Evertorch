using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The server-only test package of Milestone 13 (Coding Standards §10): the repository's content with server-only
///     values changed, such as a weaker boss and a shorter respawn, and the manifest signed again with the repository's
///     client content version, so the real client still connects to a server that loads it.
/// </summary>
internal static class DungeonPackage
{
    private const string Monsters = "monsters.json";
    private const string Maps = "maps.json";
    private const string Quests = "quests.json";

    public static Dictionary<string, byte[]> Build(params Action<Dictionary<string, byte[]>>[] edits)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string clientContentVersion = PackageFixture.ClientContentVersionOf(files);
        foreach (Action<Dictionary<string, byte[]>> edit in edits)
        {
            edit(files);
        }

        PackageFixture.RewriteManifest(files, clientContentVersion);
        return files;
    }

    public static Action<Dictionary<string, byte[]>> Weaken(string monster, int hp)
    {
        return files => PackageFixture.SetValue(files, Monsters, monster, "hp", Number(hp));
    }

    /// <summary>A monster that still swings, but for no damage: a party meets it and lives.</summary>
    public static Action<Dictionary<string, byte[]>> Disarm(string monster)
    {
        return files => PackageFixture.SetValue(files, Monsters, monster, "physicalAttack", Number(0));
    }

    /// <summary>A monster that never dodges, so even a level-1 character's swing at it lands.</summary>
    public static Action<Dictionary<string, byte[]>> Expose(string monster)
    {
        return files => PackageFixture.SetValue(files, Monsters, monster, "flee", Number(0));
    }

    /// <summary>A monster whose spells strike for 1, the least a strike deals: no magic attack.</summary>
    public static Action<Dictionary<string, byte[]>> Muffle(string monster)
    {
        return files => PackageFixture.SetValue(files, Monsters, monster, "magicAttack", Number(0));
    }

    /// <summary>A monster that casts its first skill at every decision it may.</summary>
    public static Action<Dictionary<string, byte[]>> AlwaysCast(string monster)
    {
        return files => PackageFixture.SetValue(files, Monsters, monster, "skills[0].chance", Number(1));
    }

    /// <summary>A quest that asks for fewer kills, so a test earns it in a few.</summary>
    public static Action<Dictionary<string, byte[]>> Shorten(string quest, int count)
    {
        return files => PackageFixture.SetValue(files, Quests, quest, "count", Number(count));
    }

    public static Action<Dictionary<string, byte[]>> Respawn(string map, string monster, int respawnMs)
    {
        return files => PackageFixture.SetValue(
            files,
            Maps,
            map,
            $"monsterSpawns[{SpawnIndex(files, map, monster)}].respawnMs",
            Number(respawnMs));
    }

    private static int SpawnIndex(IReadOnlyDictionary<string, byte[]> files, string map, string monster)
    {
        JsonArray definitions = JsonNode.Parse(files[Maps])!["definitions"]!.AsArray();
        JsonNode definition = definitions.Single(node => (string?)node!["id"] == map)!;
        var spawns = definition["monsterSpawns"]!.AsArray().ToList();
        int index = spawns.FindIndex(spawn => (string?)spawn!["monster"] == monster);
        return index >= 0 ? index : throw new InvalidOperationException($"{map} spawns no '{monster}'.");
    }

    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
}
