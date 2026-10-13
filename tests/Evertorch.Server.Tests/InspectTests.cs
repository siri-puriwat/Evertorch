using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Inspecting another player (Gameplay Systems §11.7; Network Protocol §9, §11): the worn weapon and armor, each with
///     its refine level and cards, read on the tick thread and sent to the asker alone; an unseen entity or one that is
///     not a player refused with 1; a dead asker refused with 3; a trade no obstacle.
/// </summary>
[TestFixture]
public sealed class InspectTests
{
    private const string Sword = "item.weapon.iron_sword";
    private const string Cloth = "item.armor.cloth";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";

    // Character 1 wears a +4 Iron Sword carrying Keen and Bane, and cloth armor; character 2 enters beside it.
    private static (TradeRig Rig, ConnectionId Worn, ConnectionId Asker) Enter()
    {
        var rig = new TradeRig(new TestServer(withNpcs: true));
        ConnectionId worn = rig.Enter(
            1,
            store =>
            {
                store.GiveItems(1, Sword, 1, 1, 5);
                long sword = store.Stored(1).Items.Last().Id;
                store.Refine(1, sword, 4);
                store.Card(1, sword, Keen, Bane);
                store.Wear(1, sword, "Weapon");
                store.GiveItems(1, Cloth, 1, 1, 5);
                store.Wear(1, store.Stored(1).Items.Last().Id, "Armor");
                store.GiveItems(1, Keen, 1, 3, 5);
            });
        ConnectionId asker = rig.Enter(2);
        rig.Server.Tick(2);
        rig.Server.Transport.ClearSent();
        return (rig, worn, asker);
    }

    private static InspectedEquipment[] Answers(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.InspectedEquipment)
            .Select(message =>
                InspectedEquipment.TryRead(message.Payload, out InspectedEquipment? read) ? read! : null!)
            .ToArray();
    }

    [TestCase("an entity never seen", 1)]
    [TestCase("an NPC", 1)]
    [TestCase("the asker itself", 1)]
    [TestCase("a dead asker", 3)]
    public void Inspect_ThatIsRefused_SaysWhy_AndSendsNothing(string refusal, int reason)
    {
        (TradeRig rig, ConnectionId worn, ConnectionId asker) = Enter();
        EntityId target = refusal switch
        {
            "an entity never seen" => new EntityId(999_999),
            "an NPC" => rig.Server.NpcOf("npc.smith").Id,
            "the asker itself" => rig.Server.PlayerOf(asker).Id,
            _ => rig.Server.PlayerOf(worn).Id
        };
        if (refusal == "a dead asker")
        {
            PlayerEntity dead = rig.Server.PlayerOf(asker);
            rig.Server.World.TryGetMap(new MapDefinitionId("map.training_ground"), out MapInstance? ground);
            rig.Server.Combat.Kill(ground!, dead, null, rig.Server.CurrentTick);
        }

        uint sequence = rig.Next(asker);
        rig.Server.SendInspectPlayer(asker, target, sequence);
        rig.Server.Tick();

        Assert.That(rig.RefusalOf(asker, sequence), Is.EqualTo((CommandRejectionReason)reason), refusal);
        Assert.That(Answers(rig.Server, asker), Is.Empty);
    }

    [Test]
    public void Inspect_OfAPlayerInView_AnswersTheAskerAlone_WithTheWornWeaponAndArmorAndTheirCards()
    {
        (TradeRig rig, ConnectionId worn, ConnectionId asker) = Enter();
        EntityId target = rig.Server.PlayerOf(worn).Id;

        rig.Server.SendInspectPlayer(asker, target, rig.Next(asker));
        rig.Server.Tick();

        InspectedEquipment answer = Answers(rig.Server, asker).Single();
        Assert.That(answer.Entity, Is.EqualTo(target));
        Assert.That(
            (answer.Weapon.Item.Value, answer.Weapon.RefineLevel, answer.Weapon.Cards.Select(card => card.Value)),
            Is.EqualTo((Sword, (byte)4, new[] { Keen, Bane })));
        Assert.That(
            (answer.Armor.Item.Value, answer.Armor.RefineLevel, answer.Armor.Cards.Count),
            Is.EqualTo((Cloth, (byte)0, 0)));
        Assert.That(Answers(rig.Server, worn), Is.Empty, "the inspected hears nothing");
        Assert.That(rig.Refusals(asker), Is.Empty);
    }

    // The review's finding (session task T18): a real player the asker cannot see, on another map, is refused with 1.
    [Test]
    public void Inspect_OfAPlayerOnAnotherMap_IsRefusedWithOne()
    {
        var rig = new TradeRig(new TestServer(withNpcs: true, withEveryMap: true, withGrotto: true));
        ConnectionId away = rig.Enter(1);
        ConnectionId asker = rig.Enter(2);
        EntityId target = rig.Server.PlayerOf(away).Id;
        rig.Server.CrossIntoTheGrotto(away);
        rig.Server.Tick(2);
        rig.Server.Transport.ClearSent();

        uint sequence = rig.Next(asker);
        rig.Server.SendInspectPlayer(asker, target, sequence);
        rig.Server.Tick();

        Assert.That(rig.RefusalOf(asker, sequence), Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(Answers(rig.Server, asker), Is.Empty);
    }

    [Test]
    public void Inspect_OfAPlayerWearingNothing_NamesTwoEmptySlots()
    {
        (TradeRig rig, ConnectionId worn, ConnectionId asker) = Enter();

        rig.Server.SendInspectPlayer(worn, rig.Server.PlayerOf(asker).Id, rig.Next(worn));
        rig.Server.Tick();

        InspectedEquipment answer = Answers(rig.Server, worn).Single();
        Assert.That((answer.Weapon.IsEmpty, answer.Armor.IsEmpty), Is.EqualTo((true, true)));
    }

    // An inspection changes nothing, so a trade does not stop it (Network Protocol §11).
    [Test]
    public void Inspect_WhileTrading_IsAnswered()
    {
        (TradeRig rig, ConnectionId worn, ConnectionId asker) = Enter();
        rig.Open(asker, worn, "Tester2", "Tester1");
        rig.Server.Transport.ClearSent();

        uint sequence = rig.Next(asker);
        rig.Server.SendInspectPlayer(asker, rig.Server.PlayerOf(worn).Id, sequence);
        rig.Server.Tick();

        Assert.That(rig.RefusalOf(asker, sequence), Is.EqualTo(CommandRejectionReason.None));
        Assert.That(Answers(rig.Server, asker), Has.Length.EqualTo(1));
    }
}
}
