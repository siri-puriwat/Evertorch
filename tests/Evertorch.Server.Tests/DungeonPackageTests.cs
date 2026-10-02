using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 13 test package (Coding Standards §10): server-only values change, and the client content version
///     stays the repository's, so the real client is still admitted.
/// </summary>
[TestFixture]
public sealed class DungeonPackageTests
{
    private const string Slime = "monster.training_slime";
    private const string Ground = "map.training_ground";

    [Test]
    public void Build_WithADisarmedMonster_LoadsItWithNoAttack()
    {
        ServerContent content = ServerContentLoader.Load(DungeonPackage.Build(DungeonPackage.Disarm(Slime)));

        Assert.That(content.Monsters[new MonsterDefinitionId(Slime)].PhysicalAttack, Is.Zero);
    }

    [Test]
    public void Build_WithAWeakerMonsterAndAShorterRespawn_LoadsThemAndKeepsTheClientVersion()
    {
        Dictionary<string, byte[]> repository = PackageFixture.BuildRepositoryPackage();
        Dictionary<string, byte[]> files = DungeonPackage.Build(
            DungeonPackage.Weaken(Slime, 1),
            DungeonPackage.Respawn(Ground, Slime, 1500));

        ServerContent content = ServerContentLoader.Load(files);
        ServerContent unchanged = ServerContentLoader.Load(repository);

        Assert.That(content.Monsters[new MonsterDefinitionId(Slime)].Hp, Is.EqualTo(1));
        MonsterSpawn spawn = content.Maps[new MapDefinitionId(Ground)].MonsterSpawns
            .Single(entry => entry.Monster == new MonsterDefinitionId(Slime));
        Assert.That(spawn.RespawnMs, Is.EqualTo(1500));
        Assert.That(content.ClientContentVersion, Is.EqualTo(unchanged.ClientContentVersion));
        Assert.That(content.ServerContentVersion, Is.Not.EqualTo(unchanged.ServerContentVersion));
    }

    [Test]
    public void Build_WithAWispExposedAndAlwaysCasting_LoadsItSo()
    {
        const string wisp = "monster.gloom_wisp";
        ServerContent content = ServerContentLoader.Load(
            DungeonPackage.Build(DungeonPackage.Expose(wisp), DungeonPackage.AlwaysCast(wisp)));

        MonsterDefinition definition = content.Monsters[new MonsterDefinitionId(wisp)];
        Assert.That((definition.Flee, definition.Skills[0].Chance), Is.EqualTo((0, 1d)));
    }

    [Test]
    public void Build_WithNoEdit_IsTheRepositoryPackage()
    {
        Dictionary<string, byte[]> repository = PackageFixture.BuildRepositoryPackage();
        Dictionary<string, byte[]> files = DungeonPackage.Build();

        Assert.That(
            ServerContentLoader.Load(files).ServerContentVersion,
            Is.EqualTo(ServerContentLoader.Load(repository).ServerContentVersion));
    }
}
}
