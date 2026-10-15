using System;
using System.Linq;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Ailments on PostgreSQL 18 (Milestone 19 verification; Gameplay Systems §9.1, §11.2; Persistence §5, §6): a
///     Remedy whose answers are lost, or sent twice, commits once and cures once; a player who dies before the answer
///     spends the Remedy and is cured of nothing; and statuses keep their time left through the reconnect grace, a map
///     change, and a save, a crash after it leaving what the save stored. Logout, a clean stop, the checkpoint on an
///     effect's last tick, a converter loaded unarmed, an unknown status, and the migration are
///     <see cref="SavedStatusEffectTests" />', <c>AilmentAcceptanceTests</c>', and the persistence suite's.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class AilmentDurabilityTests
{
    private const string Remedy = "item.consumable.remedy";

    private static readonly StatusDefinitionId Poisoned = new("status.poisoned");
    private static readonly StatusDefinitionId Silenced = new("status.silenced");
    private static readonly StatusDefinitionId Blinded = new("status.blinded");
    private static readonly MapDefinitionId Field = new("map.training_field");

    private PostgresFixture m_database = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private long Scalar(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    private static long RowOf(TestServer server, ConnectionId player, string item)
    {
        return CharacterOf(server, player).Inventory.Rows.Single(row => row.Item.Value == item).InventoryItem;
    }

    private static void Settle(TestServer server, ConnectionId player)
    {
        for (int tick = 0; tick < 200 && CharacterOf(server, player).Operation != null; tick++)
        {
            server.Tick();
        }

        Assert.That(CharacterOf(server, player).Operation, Is.Null, "the operation settled");
    }

    // Enters holding two Remedies, poisoned and blinded for a minute.
    private static ConnectionId EnterAfflicted(TestServer server, string identity, string name)
    {
        ConnectionId player = server.EnterWorldAs(identity, name);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.First(map => map.Players.Contains(server.PlayerOf(player))),
            new ItemDefinitionId(Remedy),
            2,
            server.PlayerOf(player).Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.SendPickup(player, drop.Id, 1);
        server.Tick();
        Settle(server, player);
        PlayerEntity entity = server.PlayerOf(player);
        server.StatusEffects.Apply(entity, Poisoned, default, server.World.NowMs + 60_000);
        server.StatusEffects.Apply(entity, Blinded, default, server.World.NowMs + 60_000);
        server.Tick();
        return player;
    }

    private static int StatusListsTold(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player).Count(message => message.Opcode == MessageOpcode.StatusEffects);
    }

    private long Consumes(long character)
    {
        return Scalar(
            "SELECT count(*) FROM economy_ledger "
            + $"WHERE actor_character_id = {character} AND operation_type = 'consume'");
    }

    // Every answer is lost, so the writer replays the commit and the server settles it from the ledger; the same use
    // sent again with its sequence is a duplicate. One unit spent, one ledger row, and one cure, told once.
    [Test]
    public void Remedy_WhoseAnswersAreLost_AndSentTwice_CommitsOnce_AndCuresOnce()
    {
        var store = new LosingAnswersGameStore(new PostgresGameStore(m_database.ConnectionString));
        var server = new TestServer(store: store);
        ConnectionId player = EnterAfflicted(server, "v19-lost", "RemedyLost");
        long character = CharacterOf(server, player).Character.Value;
        long remedies = RowOf(server, player, Remedy);
        PlayerEntity entity = server.PlayerOf(player);
        server.Transport.ClearSent();

        store.IsLosingAnswers = true;
        server.SendUseItem(player, remedies, 4);
        server.SendUseItem(player, remedies, 4);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);
        server.Tick(2);

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(Consumes(character), Is.EqualTo(1L), "one ledger row");
        Assert.That(Scalar($"SELECT quantity FROM inventory_items WHERE id = {remedies}"), Is.EqualTo(1L));
        Assert.That((entity.HasAilment(Ailment.Poison), entity.HasAilment(Ailment.Blind)), Is.EqualTo((false, false)));
        Assert.That(StatusListsTold(server, player), Is.EqualTo(1), "the cure told once");
    }

    // The use is queued, the player dies before its answer, and the commit settles after: the unit is spent, as the
    // command was accepted, the death ended the ailments, and the settled use cures nothing more and tells nothing.
    [Test]
    public void Remedy_WhosePlayerDiesBeforeTheAnswer_SpendsTheUnit_AndCuresNothing()
    {
        var server = new TestServer(
            store: new PostgresGameStore(m_database.ConnectionString),
            withMonsters: true,
            withMonsterAi: false);
        ConnectionId player = EnterAfflicted(server, "v19-dead", "RemedyDead");
        long character = CharacterOf(server, player).Character.Value;
        PlayerEntity entity = server.PlayerOf(player);
        MapInstance map = server.World.Maps.First(candidate => candidate.Players.Contains(entity));

        server.RunsPersistence = false;
        server.SendUseItem(player, RowOf(server, player, Remedy), 4);
        server.Tick();
        server.Combat.Kill(map, entity, map.Monsters.First(), server.CurrentTick);
        server.Tick();
        int toldAtTheDeath = StatusListsTold(server, player);
        server.RunsPersistence = true;
        Settle(server, player);
        server.Tick(2);

        Assert.That(entity.IsDead, Is.True);
        Assert.That(Consumes(character), Is.EqualTo(1L), "the unit spent once");
        Assert.That(entity.StatusEffects, Is.Empty, "the death ended them");
        Assert.That(StatusListsTold(server, player), Is.EqualTo(toldAtTheDeath), "the settled use told nothing");
    }

    // Statuses on a character who drops and comes back within the grace, crosses into the field, and is saved keep
    // their ends; one started after the save is lost with the process, and the next server loads what the save stored
    // with the time it had left (Persistence §6).
    [Test]
    public void Statuses_ThroughTheGrace_AMapChange_AndASave_KeepTheirEnds_AndACrashKeepsTheSaved()
    {
        var server = new TestServer(
            store: new PostgresGameStore(m_database.ConnectionString),
            withEveryMap: true,
            reconnectGraceMs: 30_000);
        ConnectionId player = server.EnterWorldAs("v19-keep", "AilmentKeep");
        long character = CharacterOf(server, player).Character.Value;
        PlayerEntity entity = server.PlayerOf(player);
        server.StatusEffects.Apply(entity, Silenced, default, server.World.NowMs + 60_000);
        server.StatusEffects.Apply(entity, Poisoned, default, server.World.NowMs + 60_000);
        long[] ends = entity.StatusEffects.Select(effect => effect.EndMs).ToArray();
        long silenceEnd = entity.StatusEffects.Single(effect => effect.Status == Silenced).EndMs;

        server.Disconnect(player);
        server.Tick(5);
        ConnectionId back = server.EnterWorldAs("v19-keep", "AilmentKeep");
        bool isTheSameBody = server.PlayerOf(back) == entity;
        long[] endsAfterTheGrace = entity.StatusEffects.Select(effect => effect.EndMs).ToArray();
        server.World.TryGetMap(new MapDefinitionId("map.training_ground"), out MapInstance? ground);
        entity.Position = ground!.Definition.Portals.Single().Center;
        server.TickUntil(() =>
            server.World.Maps.Any(map => map.Definition.Id == Field && map.Players.Contains(entity)));
        long[] endsAfterTheMapChange = entity.StatusEffects.Select(effect => effect.EndMs).ToArray();

        Task<int> saved = server.Admin.SaveAsync(AdminActor.LocalConsole);
        server.Tick();
        server.TickUntil(() => server.Persistence.WaitingCheckpoints == 0 && server.Persistence.PendingJobs == 0);
        long now = server.World.NowMs;
        long silenceStored = Scalar(
            "SELECT remaining_ms FROM character_status_effects "
            + $"WHERE character_id = {character} AND status_definition_id = '{Silenced.Value}'");
        server.StatusEffects.Apply(entity, Blinded, default, server.World.NowMs + 60_000);
        server.Tick(10);

        var restarted = new TestServer(store: new PostgresGameStore(m_database.ConnectionString), withEveryMap: true);
        ConnectionId again = restarted.EnterWorldAs("v19-keep", "AilmentKeep");
        PlayerEntity loaded = restarted.PlayerOf(again);

        Assert.That(isTheSameBody, Is.True, "the grace kept the body");
        Assert.That(endsAfterTheGrace, Is.EqualTo(ends), "the grace");
        Assert.That(endsAfterTheMapChange, Is.EqualTo(ends), "the map change");
        Assert.That(saved.IsCompletedSuccessfully, Is.True, "a tick ran the save");
        Assert.That(silenceStored, Is.EqualTo(silenceEnd - now).Within(100L), "the save stored the time left");
        Assert.That(
            loaded.StatusEffects.Select(effect => effect.Status).OrderBy(status => status.Value),
            Is.EqualTo(new[] { Poisoned, Silenced }),
            "the blind started after the save died with the process");
        Assert.That(
            loaded.StatusEffects.Single(effect => effect.Status == Silenced).EndMs - restarted.World.NowMs,
            Is.EqualTo(silenceStored),
            "with the time the save stored");
    }
}
}
