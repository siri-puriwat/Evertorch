using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Gloom Wisp's Numbing Spark (Gameplay Systems §9.1; owner decision 20): the first status put on someone else,
///     a debuff that takes 40 % of the player's AGI for 15 s, so it swings later and dodges less but walks as fast; a
///     recast replaces it, death ends it, and only its owner hears of it.
/// </summary>
[TestFixture]
public sealed class DebuffTests
{
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly SkillDefinitionId NumbingSpark = new("skill.numbing_spark");
    private static readonly StatusDefinitionId Numbed = new("status.numbed");

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    // The players walk into the grotto's wisp gallery, 4 m from a Gloom Wisp; the AI is off, so only the test casts.
    // The first player's AGI is 50.
    private static (TestServer Server, ConnectionId[] Players, MonsterEntity Wisp) InTheGallery(int players = 1)
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false);
        var entered = new ConnectionId[players];
        for (int index = 0; index < players; index++)
        {
            entered[index] = server.EnterWorld(index + 1);
            server.CrossIntoTheGrotto(entered[index]);
            server.PlayerOf(entered[index]).Position = new WorldPosition(12f, 0f, -19.5f + index);
        }

        PlayerEntity first = server.PlayerOf(entered[0]);
        first.SetPrimary(new PrimaryStats(5, 50, 5, 5, 5, 5));
        server.Stats.Recalculate(first, server.Content.Jobs[first.Job]);
        MonsterEntity wisp = GrottoOf(server).Monsters
            .First(monster => monster.Definition.Id.Value == "monster.gloom_wisp");
        wisp.Position = new WorldPosition(16f, 0f, -19.5f);
        server.Tick();
        return (server, entered, wisp);
    }

    // The wisp casts at the player; the cast's 800 ms pass within the second the test then ticks.
    private static void Numb(TestServer server, MonsterEntity wisp, PlayerEntity player)
    {
        MapInstance map = GrottoOf(server);
        Assert.That(server.Combat.CanMonsterCast(map, wisp, NumbingSpark, player, server.CurrentTick), Is.True);
        server.Combat.BeginMonsterCast(map, wisp, NumbingSpark, player, server.CurrentTick);
        server.Tick(TestServer.TickRate);
    }

    [Test]
    public void NumbingSpark_IsHeardByItsOwnerAlone()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity wisp) = InTheGallery(2);
        server.Transport.ClearSent();

        Numb(server, wisp, server.PlayerOf(players[0]));

        StatusEffects told = server.Transport.ControlSentTo(players[0])
            .Where(message => message.Opcode == MessageOpcode.StatusEffects)
            .Select(message => StatusEffects.TryRead(message.Payload, out StatusEffects? read) ? read! : null)
            .Last()!;
        Assert.That(told.Effects.Select(entry => entry.Status), Is.EqualTo(new[] { Numbed }));
        Assert.That(
            server.Transport.ControlOpcodesSentTo(players[1]),
            Has.None.EqualTo(MessageOpcode.StatusEffects),
            "the other player hears nothing of it");
    }

    [Test]
    public void NumbingSpark_Recast_ReplacesTheEffect_AndDeathEndsIt()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity wisp) = InTheGallery();
        PlayerEntity player = server.PlayerOf(players[0]);

        Numb(server, wisp, player);
        long firstEnd = player.StatusEffects.Single().EndMs;
        server.Tick(8 * TestServer.TickRate);
        Numb(server, wisp, player);
        ActiveStatusEffect recast = player.StatusEffects.Single();
        server.Combat.Kill(GrottoOf(server), player, wisp, server.CurrentTick);

        Assert.That(recast.EndMs, Is.GreaterThan(firstEnd), "one effect, renewed");
        Assert.That(player.StatusEffects, Is.Empty, "death ends it");
    }

    [Test]
    public void NumbingSpark_TakesFortyPercentOfTheAgi_ForFifteenSeconds_SlowingTheAttacksAndTheDodge_NotTheWalk()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity wisp) = InTheGallery();
        PlayerEntity player = server.PlayerOf(players[0]);
        DerivedStats before = player.Stats;
        float walk = player.MovementSpeed;

        Numb(server, wisp, player);
        DerivedStats numbed = player.Stats;
        ActiveStatusEffect effect = player.StatusEffects.Single();
        server.Tick(15 * TestServer.TickRate);

        Assert.That((effect.Status, effect.StatPercent), Is.EqualTo((Numbed, new StatPercentages(0, -40, 0, 0, 0, 0))));
        Assert.That(before.Flee - numbed.Flee, Is.EqualTo(20), "AGI 50 falls to 30");
        Assert.That(numbed.AttackSpeed, Is.LessThan(before.AttackSpeed), "a longer swing interval");
        Assert.That(player.MovementSpeed, Is.EqualTo(walk), "the walk keeps its speed");
        Assert.That(player.StatusEffects, Is.Empty, "15 s later it is over");
        Assert.That(player.Stats.Flee, Is.EqualTo(before.Flee));
    }
}
}
