using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Converters (Gameplay Systems §9.1, §11.2): a use needs a worn weapon; once its commit settles, its status gives
///     the player's swings and weapon skills its element for twenty minutes, replacing another converter's, and no
///     health is sent; a change of the worn weapon ends it, a change of the armor does not.
/// </summary>
[TestFixture]
public sealed class ConverterTests
{
    private const string Fire = "item.consumable.fire_converter";
    private const string Wind = "item.consumable.wind_converter";
    private const string Sword = "item.weapon.training_sword";
    private const string IronSword = "item.weapon.iron_sword";
    private const string Cloth = "item.armor.cloth";
    private const int TwentyMinutes = 1_200_000;

    private static readonly StatusDefinitionId FireWeapon = new("status.fire_weapon");
    private static readonly StatusDefinitionId WindWeapon = new("status.wind_weapon");

    private delegate bool TryRead<T>(byte[] payload, out T message);

    private static List<T> Sent<T>(TestServer server, ConnectionId player, MessageOpcode opcode, TryRead<T> read)
    {
        var messages = new List<T>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == opcode && read(message.Payload, out T decoded))
            {
                messages.Add(decoded);
            }
        }

        return messages;
    }

    private static CommandRejectionReason[] Rejections(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.CommandRejected, (byte[] bytes, out CommandRejected read) =>
                CommandRejected.TryRead(bytes, out read))
            .Select(rejected => rejected.Reason)
            .ToArray();
    }

    private static string[] LastStatusList(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.StatusEffects, (byte[] bytes, out StatusEffects read) =>
            {
                bool isRead = StatusEffects.TryRead(bytes, out StatusEffects? message);
                read = message!;
                return isRead;
            })
            .Last()
            .Effects.Select(effect => effect.Status.Value)
            .ToArray();
    }

    private static long RowOf(TestServer server, string item)
    {
        return server.Store.Stored(1).Items.First(stored => stored.ItemDefinitionId == item).Id;
    }

    private static int QuantityOf(TestServer server, string item)
    {
        return server.Store.Stored(1).Items.Where(stored => stored.ItemDefinitionId == item).Sum(row => row.Quantity);
    }

    // Character 1 enters holding five of each converter and one of each extra item, a training sword worn when armed.
    private static ConnectionId Enter(TestServer server, bool isArmed, params string[] extra)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, Fire, 1, 5, 5);
        server.Store.GiveItems(1, Wind, 1, 5, 5);
        foreach (string item in extra)
        {
            server.Store.GiveItems(1, item, 1, 1, 5);
        }

        if (isArmed)
        {
            server.Store.GiveItems(1, Sword, 1, 1, 5);
            server.Store.Wear(1, server.Store.Stored(1).Items.Last().Id, "Weapon");
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    private static void Settle(TestServer server, ConnectionId player)
    {
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);
    }

    private static void Use(TestServer server, ConnectionId player, string converter, uint sequence)
    {
        server.SendUseItem(player, RowOf(server, converter), sequence);
        Settle(server, player);
    }

    [Test]
    public void AConverter_OnceItsCommitSettles_GivesTheSwingsItsElementForTwentyMinutes_AndSendsNoHealth()
    {
        var server = new TestServer();
        ConnectionId player = Enter(server, true);
        PlayerEntity entity = server.PlayerOf(player);
        server.Transport.ClearSent();

        server.SendUseItem(player, RowOf(server, Fire), 1);
        bool isActiveBeforeTheCommit = entity.StatusEffects.Count > 0;
        Settle(server, player);
        long now = (long)(server.CurrentTick - 1) * 1000 / TestServer.TickRate;

        Assert.That(isActiveBeforeTheCommit, Is.False, "nothing starts before the commit settles");
        Assert.That(entity.StatusEffects.Select(effect => effect.Status), Is.EqualTo(new[] { FireWeapon }));
        Assert.That(entity.StatusEffects.Single().AttackElement, Is.EqualTo(Element.Fire));
        Assert.That(entity.StatusEffects.Single().EndMs, Is.InRange(now + TwentyMinutes - 100, now + TwentyMinutes));
        Assert.That(entity.AttackElement, Is.EqualTo(Element.Fire));
        Assert.That(LastStatusList(server, player), Is.EqualTo(new[] { FireWeapon.Value }));
        Assert.That(
            server.Transport.ControlOpcodesSentTo(player),
            Has.No.Member(MessageOpcode.CharacterHealth),
            "a converter restores nothing");
        Assert.That(QuantityOf(server, Fire), Is.EqualTo(4));
    }

    [Test]
    public void AConverter_WithNoWeaponWorn_IsRefusedWithThree_AndSpendsNothing()
    {
        var server = new TestServer();
        ConnectionId player = Enter(server, false);
        server.Transport.ClearSent();

        server.SendUseItem(player, RowOf(server, Fire), 1);
        server.Tick(2);

        Assert.That(Rejections(server, player), Is.EqualTo(new[] { CommandRejectionReason.NotAllowedNow }));
        Assert.That(server.PlayerOf(player).StatusEffects, Is.Empty);
        Assert.That(QuantityOf(server, Fire), Is.EqualTo(5));
    }

    [Test]
    public void AConvertersElement_EndsWithAnUnequipOrASwap_ButNotWithArmor()
    {
        var server = new TestServer();
        ConnectionId player = Enter(server, true, Cloth, IronSword);
        PlayerEntity entity = server.PlayerOf(player);

        Use(server, player, Fire, 1);
        server.SendEquip(player, RowOf(server, Cloth), 2);
        Settle(server, player);
        Element afterArmor = entity.AttackElement;
        server.SendEquip(player, RowOf(server, IronSword), 3);
        Settle(server, player);
        Element afterSwap = entity.AttackElement;
        string[] toldAfterSwap = LastStatusList(server, player);
        Use(server, player, Wind, 4);
        server.SendUnequip(player, EquipmentSlot.Weapon, 5);
        Settle(server, player);

        Assert.That(afterArmor, Is.EqualTo(Element.Fire), "armor is not the weapon");
        Assert.That(afterSwap, Is.EqualTo(Element.Neutral), "a swap ends it");
        Assert.That(toldAfterSwap, Is.Empty, "and the owner hears it");
        Assert.That((entity.AttackElement, entity.StatusEffects.Count), Is.EqualTo((Element.Neutral, 0)));
        Assert.That(LastStatusList(server, player), Is.Empty, "the owner hears it end");
    }

    [Test]
    public void ASecondConverter_EndsTheFirstsElement_AndTakesItsPlace()
    {
        var server = new TestServer();
        ConnectionId player = Enter(server, true);
        PlayerEntity entity = server.PlayerOf(player);

        Use(server, player, Fire, 1);
        Use(server, player, Wind, 2);

        Assert.That(entity.StatusEffects.Select(effect => effect.Status), Is.EqualTo(new[] { WindWeapon }));
        Assert.That(entity.AttackElement, Is.EqualTo(Element.Wind));
        Assert.That(LastStatusList(server, player), Is.EqualTo(new[] { WindWeapon.Value }));
        Assert.That((QuantityOf(server, Fire), QuantityOf(server, Wind)), Is.EqualTo((4, 4)));
    }
}
}
