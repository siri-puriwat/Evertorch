using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The dungeon's logs name characters by number alone (System Architecture §10; Milestone 13 hardening): a boss's
///     spawn and fall, its most valuable player's prize in the bag or at its feet, a prize whose answer was lost, and the
///     operator's respawn, which the audit records, never write a character's name.
/// </summary>
[TestFixture]
public sealed class DungeonLogTests
{
    private const string Name = "Annabelle";
    private const string Gel = "item.material.slime_gel";

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");

    // Signs in as its own account, creates the character, fills its bag when asked, and enters the grotto.
    private static ConnectionId EnterTheGrotto(TestServer server, bool isBagFull)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, "dev:logs-anna");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendCreateCharacter(connection, Name);
        server.TickUntil(() => server.SessionOf(connection).Characters!.Any(owned => owned.Name == Name));
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == Name).Id;
        if (isBagFull)
        {
            server.Store.GiveItems(character, Gel, PickupSystem.MaxInventoryRows, 1, 0);
        }

        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        server.CrossIntoTheGrotto(connection);
        return connection;
    }

    private static string Console(TestServer server, string line)
    {
        var output = new StringWriter();
        Task answered = new AdminConsole(server.Admin).Execute(line, TextWriter.Synchronized(output));
        server.Tick();
        Assert.That(answered.Wait(TimeSpan.FromSeconds(10)), Is.True, $"'{line}' was answered");
        return output.ToString().Trim();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BossAndPrizeLogs_NameTheMvpByItsNumberAlone(bool isBagFull)
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            dropRandom: new ScriptedRandom(0));
        ConnectionId mvp = EnterTheGrotto(server, isBagFull);
        PlayerEntity player = server.PlayerOf(mvp);
        CharacterSession character = server.SessionOf(mvp).Character!;
        server.Store.AmbiguousGrantFailures = isBagFull ? 0 : 100;
        server.World.TryGetMap(Grotto, out MapInstance? grotto);
        MonsterEntity monarch = grotto!.Monsters.Single(monster => monster.Definition.IsBoss);

        monarch.LogMvpDealt(player.Character, 500);
        server.Combat.Kill(grotto, monarch, player, server.CurrentTick);
        for (int tick = 0; tick < 5 * TestServer.TickRate && character.HasInventoryWork; tick++)
        {
            server.Tick();
        }

        string respawned = Console(server, "boss respawn");
        string logged = string.Join("\n", server.AllLogText());
        string number = character.Character.Value.ToString(CultureInfo.InvariantCulture);

        Assert.That(character.HasInventoryWork, Is.False, "the prize settled");
        Assert.That(respawned, Is.EqualTo("Brought back: monster.slime_monarch."));
        Assert.That(
            server.BossLog.Entries.Select(entry => entry.EventId.Id),
            Is.SupersetOf(new[] { 1023, 1024 }),
            "the spawn and the fall");
        Assert.That(
            server.RewardLog.Entries.Select(entry => entry.EventId.Id),
            Is.EqualTo(isBagFull ? new[] { 1026 } : new[] { 4012, 1025 }));
        Assert.That(
            server.BossLog.Entries.Single(entry => entry.EventId.Id == 1024).Fields["Character"],
            Is.EqualTo(number));
        Assert.That(
            server.RewardLog.Entries.Select(entry => entry.Fields["Character"]?.ToString()),
            Has.All.EqualTo(number));
        Assert.That(
            server.AuditLogger.Entries.Single(entry => entry.EventId.Name == "OperatorBossRespawned")
                .Fields["Bosses"],
            Is.EqualTo("monster.slime_monarch"));
        Assert.That(logged, Does.Not.Contain(Name).And.Not.Contain(Name.ToLowerInvariant()));
    }
}
}
