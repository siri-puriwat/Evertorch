using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     The live classes' copies of the server package (Coding Standards §10): a seeded copy differs from the
///     repository's package only by the Ashen Quarry's spawns, kept when a test asks for them, and still names the client
///     content version the real client holds.
/// </summary>
public sealed class LiveTestPackageTests
{
    private const string Maps = "maps.json";
    private const string Manifest = "manifest.json";

    private static string ReadMaps(string folder)
    {
        return File.ReadAllText(Path.Combine(folder, Maps));
    }

    private static string ClientVersionIn(string folder)
    {
        const string key = "\"clientContentVersion\":";
        string manifest = File.ReadAllText(Path.Combine(folder, Manifest)).Replace(" ", string.Empty);
        int start = manifest.IndexOf(key, StringComparison.Ordinal) + key.Length + 1;
        return manifest.Substring(start, manifest.IndexOf('"', start) - start);
    }

    [Test]
    public void Seeded_TakesOffTheQuarrysSpawnsAlone_AndKeepsTheClientsVersion()
    {
        if (!LiveServer.IsBuilt())
        {
            Assert.Inconclusive(LiveServer.MissingPrerequisites);
        }

        using var seeded = LiveTestPackage.Seeded();
        using var whole =
            LiveTestPackage.WithMonster(null, new Dictionary<string, string>(), withQuarry: true);
        string raw = ReadMaps(LiveServer.ContentPath);
        string stripped = ReadMaps(seeded.Path);
        int prefix = 0;
        while (prefix < stripped.Length && raw[prefix] == stripped[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < stripped.Length - prefix
               && raw[raw.Length - 1 - suffix] == stripped[stripped.Length - 1 - suffix])
        {
            suffix++;
        }

        string removed = raw.Substring(prefix, raw.Length - suffix - prefix);
        int quarry = raw.IndexOf("\"id\": \"map.ashen_quarry\"", StringComparison.Ordinal);
        int spawns = raw.IndexOf("\"monsterSpawns\": [", quarry, StringComparison.Ordinal);
        int next = raw.IndexOf("\"id\": \"map.", quarry + 1, StringComparison.Ordinal);

        Assert.That(quarry, Is.GreaterThanOrEqualTo(0), "the quarry in the package");
        Assert.That(prefix, Is.GreaterThan(spawns), "nothing before the quarry's spawns changed");
        Assert.That(
            removed.Length == 0 || next < 0 || raw.Length - suffix <= next,
            Is.True,
            "nothing after its definition");
        Assert.That(stripped.Substring(prefix, stripped.Length - suffix - prefix).Trim(), Is.Empty, "only removed");
        Assert.That(removed, Does.Not.Contain("\"portals\"").And.Not.Contain("\"id\""), "the spawns alone");
        Assert.That(stripped, Does.Contain("\"monsterSpawns\": []"));
        Assert.That(ReadMaps(whole.Path), Is.EqualTo(raw), "kept when asked for");
        Assert.That(
            Directory.GetFiles(seeded.Path)
                .Select(Path.GetFileName)
                .Where(name => name != Maps && name != Manifest)
                .Where(name => !File.ReadAllBytes(Path.Combine(seeded.Path, name))
                    .SequenceEqual(File.ReadAllBytes(Path.Combine(LiveServer.ContentPath, name)))),
            Is.Empty,
            "every other file as it was");
        Assert.That(ClientVersionIn(seeded.Path), Is.EqualTo(ClientVersionIn(LiveServer.ContentPath)));
    }
}
}
