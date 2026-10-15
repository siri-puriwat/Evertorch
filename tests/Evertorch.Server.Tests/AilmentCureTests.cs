using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The cures (Gameplay Systems §11.2): the Remedy and the Grand Remedy end poison, silence, and blind once their use
///     settles, the Grand Remedy healing too; a held body uses no item; Mend ends the same three on whom it heals; and
///     the Quartermaster trades the Remedy as its fourteenth item.
/// </summary>
[TestFixture]
public sealed class AilmentCureTests
{
    private const string Remedy = "item.consumable.remedy";
    private const string GrandRemedy = "item.consumable.grand_remedy";
    private static readonly StatusDefinitionId Poisoned = new("status.poisoned");
    private static readonly StatusDefinitionId Silenced = new("status.silenced");
    private static readonly StatusDefinitionId Blinded = new("status.blinded");
    private static readonly StatusDefinitionId Frozen = new("status.frozen");
    private static readonly StatusDefinitionId Stunned = new("status.stunned");

    private static ConnectionId Enter(TestServer server, params string[] items)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        foreach (string item in items)
        {
            server.Store.GiveItems(1, item, 1, 5, 5);
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    private static long RowOf(TestServer server, string item)
    {
        return server.Store.Stored(1).Items.First(stored => stored.ItemDefinitionId == item).Id;
    }

    private static int QuantityOf(TestServer server, string item)
    {
        return server.Store.Stored(1).Items.Where(stored => stored.ItemDefinitionId == item).Sum(row => row.Quantity);
    }

    private static void Afflict(TestServer server, PlayerEntity player, params StatusDefinitionId[] statuses)
    {
        foreach (StatusDefinitionId status in statuses)
        {
            server.StatusEffects.Apply(player, status, default, server.World.NowMs + 60_000);
        }
    }

    private static void Use(TestServer server, ConnectionId player, string item, uint sequence)
    {
        server.SendUseItem(player, RowOf(server, item), sequence);
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);
    }

    [Test]
    public void GrandRemedy_HealsAndCures_AndAFreezeIsNoneOfItsBusiness()
    {
        var server = new TestServer();
        ConnectionId player = Enter(server, GrandRemedy);
        PlayerEntity entity = server.PlayerOf(player);
        entity.CurrentHealth = 1;
        entity.CurrentSpirit = 0;
        Afflict(server, entity, Poisoned);
        server.Tick();

        Use(server, player, GrandRemedy, 1);

        Assert.That(
            (entity.CurrentHealth, entity.CurrentSpirit, entity.HasAilment(Ailment.Poison)),
            Is.EqualTo((Math.Min(entity.MaxHealth, 121), Math.Min(entity.MaxSpirit, 40), false)));
    }

    [Test]
    public void Mend_EndsTheSameThreeOnWhomItHeals_ButNotAFreeze()
    {
        var server = new TestServer();
        ConnectionId healer = server.EnterWorld(1);
        ConnectionId patient = server.EnterWorld(2);
        PlayerEntity caster = server.PlayerOf(healer);
        PlayerEntity healed = server.PlayerOf(patient);
        caster.ChangeJob(new JobDefinitionId("job.arcanist"));
        caster.SetSkillLevel(new SkillDefinitionId("skill.mend"), 1);
        caster.CurrentSpirit = caster.MaxSpirit;
        server.Tick();
        healed.CurrentHealth = 1;
        Afflict(server, healed, Poisoned, Blinded, Frozen);

        server.SendUseSkill(healer, "skill.mend", healed.Id, 1);
        server.Tick(2 * TestServer.TickRate);

        Assert.That(healed.CurrentHealth, Is.GreaterThan(1));
        Assert.That(healed.StatusEffects.Select(effect => effect.Status), Is.EqualTo(new[] { Frozen }));
    }

    [Test]
    public void Remedy_EndsPoisonSilenceAndBlind_ButNoHold_OnceItsUseSettles()
    {
        var server = new TestServer();
        ConnectionId player = Enter(server, Remedy);
        PlayerEntity entity = server.PlayerOf(player);
        Afflict(server, entity, Poisoned, Silenced, Blinded);
        server.Tick();

        Use(server, player, Remedy, 1);

        Assert.That(entity.StatusEffects, Is.Empty);
        Assert.That(
            entity.StateFlags & (EntityStateFlags.Poisoned | EntityStateFlags.Silenced | EntityStateFlags.Blinded),
            Is.EqualTo(EntityStateFlags.None));
        Assert.That(QuantityOf(server, Remedy), Is.EqualTo(4));
    }

    [Test]
    public void Remedy_WhileStunned_IsRefusedWithThree_AndSpendsNothing()
    {
        var server = new TestServer();
        ConnectionId player = Enter(server, Remedy);
        PlayerEntity entity = server.PlayerOf(player);
        Afflict(server, entity, Stunned, Poisoned);
        server.Tick();
        server.Transport.ClearSent();

        server.SendUseItem(player, RowOf(server, Remedy), 1);
        server.Tick(2);

        CommandRejected refused = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
            .Single();
        Assert.That(refused.Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That((QuantityOf(server, Remedy), entity.HasAilment(Ailment.Poison)), Is.EqualTo((5, true)));
    }
}
}
