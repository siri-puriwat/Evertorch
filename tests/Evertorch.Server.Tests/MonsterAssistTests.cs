using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Grotto Crawler's call to its kin (Gameplay Systems §10; the monster AI research note's vectors): when one
///     begins a swing, its idle kin of the same kind within 11 m that see it take its target and chase, each answering
///     at most once a second and each calling at most once a second; a monster with a target, or one walking home,
///     ignores the call.
/// </summary>
[TestFixture]
public sealed class MonsterAssistTests
{
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly WorldPosition Caller = new(-20f, 0f, 10f);

    // Far off in the crawler hall's north-west corner and out of reach of every call in these tests.
    private static readonly WorldPosition Aside = new(-28f, 0f, 27f);

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    // The player walks through the ground and the field into the grotto. Its crawlers are still idle this soon after
    // they spawned; all of them stand aside until a test places them.
    private static (TestServer Server, PlayerEntity Player, MonsterEntity[] Crawlers) InTheGrotto()
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            combatRandom: new SureHitRandom());
        ConnectionId connection = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(connection);
        player.CurrentHealth = 1_000_000;
        server.CrossIntoTheGrotto(connection);
        Assert.That(server.SessionOf(connection).Character!.Map.Definition.Id, Is.EqualTo(Grotto), "crossed");
        MonsterEntity[] crawlers = GrottoOf(server).Monsters
            .Where(monster => monster.Definition.Id.Value == "monster.grotto_crawler")
            .ToArray();
        Assert.That(crawlers, Has.Length.EqualTo(6));
        foreach (MonsterEntity crawler in crawlers)
        {
            Assert.That(crawler.Brain.State, Is.EqualTo(MonsterAiState.Idle));
            crawler.Position = Aside;
        }

        return (server, player, crawlers);
    }

    // The caller takes the player at arm's length as its target, so the combat phase begins its swing.
    private static void Engage(PlayerEntity player, MonsterEntity caller, WorldPosition at)
    {
        caller.Position = at;
        player.Position = new WorldPosition(at.X - 1.2f, 0f, at.Z);
        caller.Target = player.Id;
        caller.Combat.IsAutoAttacking = true;
        caller.Brain.State = MonsterAiState.Chase;
    }

    private static MonsterAiSystem AiOf(TestServer server)
    {
        return server.Phases.OfType<MonsterAiSystem>().Single();
    }

    [TestCase(10.9f, true)]
    [TestCase(11.1f, false)]
    public void Swing_OfACrawler_BringsItsIdleKinWithinElevenMetres(float distance, bool isJoined)
    {
        (TestServer server, PlayerEntity player, MonsterEntity[] crawlers) = InTheGrotto();
        Engage(player, crawlers[0], Caller);
        crawlers[1].Position = new WorldPosition(Caller.X + distance, 0f, Caller.Z);

        server.Tick(2);

        Assert.That(crawlers[0].Combat.IsSwinging, Is.True, "the caller swings");
        Assert.That(crawlers[1].Target, Is.EqualTo(isJoined ? player.Id : default));
        Assert.That(crawlers.Skip(2).Select(crawler => crawler.Target), Is.All.EqualTo(default(EntityId)));
    }

    [Test]
    public void CallKin_AtMostOnceASecond_PerCallerAndPerKin_AndNeverWhileTheKinWalksHome()
    {
        (TestServer server, PlayerEntity player, MonsterEntity[] crawlers) = InTheGrotto();
        MapInstance map = GrottoOf(server);
        MonsterAiSystem ai = AiOf(server);
        MonsterEntity caller = crawlers[0];
        MonsterEntity other = crawlers[1];
        MonsterEntity first = crawlers[2];
        MonsterEntity second = crawlers[3];
        MonsterEntity homeward = crawlers[4];
        Engage(player, caller, Caller);
        first.Position = new WorldPosition(Caller.X + 5f, 0f, Caller.Z);
        homeward.Position = new WorldPosition(Caller.X, 0f, Caller.Z + 5f);
        homeward.Brain.State = MonsterAiState.ReturnHome;

        int atFirst = ai.CallKin(map, caller, 100_000);
        second.Position = new WorldPosition(Caller.X + 6f, 0f, Caller.Z);
        int withinTheSecond = ai.CallKin(map, caller, 100_999);
        int aSecondLater = ai.CallKin(map, caller, 101_000);
        first.Target = default;
        first.Brain.State = MonsterAiState.Idle;
        Engage(player, other, new WorldPosition(Caller.X + 2f, 0f, Caller.Z - 2f));
        int beforeTheKinMayAnswerAgain = ai.CallKin(map, other, 100_500);
        int onceItMay = ai.CallKin(map, other, 101_500);

        Assert.That((atFirst, withinTheSecond, aSecondLater), Is.EqualTo((1, 0, 1)));
        Assert.That((beforeTheKinMayAnswerAgain, onceItMay), Is.EqualTo((0, 1)));
        Assert.That(
            (first.Target, second.Target, homeward.Target),
            Is.EqualTo((player.Id, player.Id, default(EntityId))));
        Assert.That(first.Brain.State, Is.EqualTo(MonsterAiState.Chase));
    }

    [Test]
    public void CallKin_ForATargetThatIsGone_CallsNoOne()
    {
        (TestServer server, PlayerEntity player, MonsterEntity[] crawlers) = InTheGrotto();
        Engage(player, crawlers[0], Caller);
        crawlers[1].Position = new WorldPosition(Caller.X + 3f, 0f, Caller.Z);
        server.Combat.Kill(GrottoOf(server), player, null, server.CurrentTick);

        int answered = AiOf(server).CallKin(GrottoOf(server), crawlers[0], 100_000);

        Assert.That(answered, Is.Zero);
        Assert.That(crawlers[1].Target, Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void Swing_OfACrawler_LeavesAKinBehindAPillar_AndBringsOneInSight()
    {
        (TestServer server, PlayerEntity player, MonsterEntity[] crawlers) = InTheGrotto();
        var caller = new WorldPosition(-22f, 0f, 12.5f);
        Engage(player, crawlers[0], caller);
        crawlers[1].Position = new WorldPosition(-15f, 0f, 12.5f);
        crawlers[2].Position = new WorldPosition(-22f, 0f, 5.5f);

        server.Tick(2);

        Assert.That(crawlers[1].Target, Is.EqualTo(default(EntityId)), "the pillar at (-18.5, 12.5) hides it");
        Assert.That(crawlers[2].Target, Is.EqualTo(player.Id), "7 m off and in sight");
    }
}
}
