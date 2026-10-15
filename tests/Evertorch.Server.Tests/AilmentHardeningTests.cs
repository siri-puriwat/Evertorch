using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The ailments at their limits (Gameplay Systems §9.1, §11.2, §16): a held or silenced player's command spam is
///     refused one by one and starts nothing; an item is never used while held; Mend never cures a monster; a logout
///     while held saves the hold with its time left; and an open trade goes on while a trader is held. The wire's
///     bounds and the content's checks are the protocol and tools suites' (T4, T5).
/// </summary>
[TestFixture]
public sealed class AilmentHardeningTests
{
    private const string Remedy = "item.consumable.remedy";
    private const string Gel = "item.material.slime_gel";
    private static readonly StatusDefinitionId Stunned = new("status.stunned");
    private static readonly StatusDefinitionId Frozen = new("status.frozen");
    private static readonly StatusDefinitionId Silenced = new("status.silenced");
    private static readonly StatusDefinitionId Poisoned = new("status.poisoned");

    private static void Inflict(TestServer server, WorldEntity entity, StatusDefinitionId status, long durationMs)
    {
        server.StatusEffects.Apply(entity, status, default, server.World.NowMs + durationMs);
    }

    private static CommandRejectionReason[] Refusals(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .ToArray();
    }

    private static int Count(TestServer server, ConnectionId connection, MessageOpcode opcode)
    {
        return server.Transport.SentTo(connection).Count(message => message.Opcode == opcode);
    }

    // A Remedy used while frozen is refused with 3 and stays in the bag; the use commits nothing.
    [Test]
    public void AnItem_WhileHeld_IsRefused_AndKept()
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, Remedy, 1, 3, 5);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        PlayerEntity entity = server.PlayerOf(player);
        Inflict(server, entity, Poisoned, 60_000);
        Inflict(server, entity, Frozen, 60_000);
        server.Transport.ClearSent();

        server.SendUseItem(player, server.Store.Stored(1).Items.Single().Id, 1);
        server.Tick(3);

        Assert.That(Refusals(server, player), Is.EqualTo(new[] { CommandRejectionReason.NotAllowedNow }));
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(3));
        Assert.That(entity.HasAilment(Ailment.Poison), Is.True, "nothing cured");
        Assert.That(server.SessionOf(player).Character!.Operation, Is.Null);
    }

    // A trade opens, then one trader is frozen: its offer, lock, and confirm still work, and the gel changes hands.
    [Test]
    public void AnOpenTrade_GoesOn_WhileATraderIsHeld()
    {
        var rig = new TradeRig();
        ConnectionId seven = rig.Enter(7, store => store.GiveItems(7, Gel, 1, 3, 5));
        ConnectionId eight = rig.Enter(8);
        rig.Open(seven, eight, "Tester7", "Tester8");
        Inflict(rig.Server, rig.Server.PlayerOf(seven), Frozen, 60_000);
        long row = rig.Server.Store.Stored(7).Items.Single().Id;

        uint offer = rig.Offer(seven, row, 3);
        uint lockSeven = rig.Lock(seven);
        rig.Lock(eight);
        uint confirm = rig.Confirm(seven);
        rig.Confirm(eight);
        rig.Server.TickUntil(() => rig.Server.Trades.OpenTrades == 0 && rig.Server.Persistence.PendingJobs == 0);
        rig.Server.Tick(2);

        Assert.That(
            new[] { offer, lockSeven, confirm }.Select(sequence => rig.RefusalOf(seven, sequence)),
            Is.All.EqualTo(CommandRejectionReason.None));
        Assert.That(rig.Server.Store.Stored(8).Items.Single().Quantity, Is.EqualTo(3), "the gel moved");
    }

    // Twenty attacks, skills, and pickups from a stunned player, then twenty skills once only silenced: each refused
    // with 3, none started, and the player still in the world.
    [Test]
    public void CommandSpam_WhileHeldOrSilenced_IsRefusedOneByOne_AndStartsNothing()
    {
        var rig = new CombatRig();
        TestServer server = rig.Server;
        Inflict(server, rig.Entity, Stunned, 2000);
        server.Transport.ClearSent();
        uint sequence = 0;
        for (int round = 0; round < 20; round++)
        {
            server.SendAttack(rig.Player, rig.Slime.Id, ++sequence);
            server.SendUseSkill(rig.Player, "skill.first_aid", default, ++sequence);
            server.SendPickup(rig.Player, new EntityId(987_654), ++sequence);
            server.Tick();
        }

        CommandRejectionReason[] held = Refusals(server, rig.Player);
        int swingsHeld = Count(server, rig.Player, MessageOpcode.AttackStarted);
        int castsHeld = Count(server, rig.Player, MessageOpcode.SkillCastStarted);
        server.Tick(TestServer.TickRate * 2);
        Inflict(server, rig.Entity, Silenced, 60_000);
        server.Transport.ClearSent();
        for (int round = 0; round < 20; round++)
        {
            server.SendUseSkill(rig.Player, "skill.first_aid", default, ++sequence);
            server.Tick();
        }

        Assert.That(held, Has.Length.EqualTo(60).And.All.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That((swingsHeld, castsHeld), Is.EqualTo((0, 0)));
        Assert.That(Refusals(server, rig.Player), Has.Length.EqualTo(20).And.All.EqualTo(
            CommandRejectionReason.NotAllowedNow));
        Assert.That(Count(server, rig.Player, MessageOpcode.SkillCastStarted), Is.Zero);
        Assert.That(server.SessionOf(rig.Player).State, Is.EqualTo(SessionState.InWorld), "refused, not dropped");
    }

    // A stunned player logs out: the logout is taken, its checkpoint saves the stun with its time left, and the
    // character enters held again.
    [Test]
    public void Logout_WhileHeld_SavesTheHold_AndTheCharacterEntersHeld()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(1);
        Inflict(server, server.PlayerOf(player), Stunned, 30_000);
        server.Tick();
        server.Transport.ClearSent();

        server.SendLogout(player, 1);
        server.Tick(3);
        int stored = server.Store.Stored(1).StatusEffects.Single(effect => effect.StatusDefinitionId == Stunned.Value)
            .RemainingMs;
        ConnectionId again = server.Connect();
        server.SignInWithCharacter(again, 1);
        server.SendEnterWorld(again, 1);
        server.TickUntil(() => server.SessionOf(again).State == SessionState.InWorld);

        Assert.That(Refusals(server, player), Is.Empty, "the logout is taken");
        Assert.That(stored, Is.InRange(29_000, 30_000));
        Assert.That(server.PlayerOf(again).IsHeld, Is.True, "it enters held");
    }

    // Mend names a poisoned slime: it heals players alone, so it is refused and the slime stays poisoned.
    [Test]
    public void Mend_OnAMonster_IsRefused_AndCuresNothing()
    {
        var rig = new CombatRig();
        TestServer server = rig.Server;
        rig.Entity.ChangeJob(new JobDefinitionId("job.arcanist"));
        rig.Entity.SetSkillLevel(new SkillDefinitionId("skill.mend"), 1);
        rig.Entity.CurrentSpirit = rig.Entity.MaxSpirit;
        server.Tick();
        Inflict(server, rig.Slime, Poisoned, 60_000);
        server.Transport.ClearSent();

        server.SendUseSkill(rig.Player, "skill.mend", rig.Slime.Id, 1);
        server.Tick(2 * TestServer.TickRate);

        Assert.That(Refusals(server, rig.Player), Has.Length.EqualTo(1).And.None.EqualTo(CommandRejectionReason.None));
        Assert.That(Count(server, rig.Player, MessageOpcode.SkillCastStarted), Is.Zero);
        Assert.That(rig.Slime.HasAilment(Ailment.Poison), Is.True);
    }
}
}
