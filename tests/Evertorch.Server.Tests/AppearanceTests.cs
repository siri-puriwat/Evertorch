using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What others see a player wear (Gameplay Systems §11.1; Network Protocol §6, §9): each spawn of a player is
///     followed by its appearance, the items of the eight positions and a revision, to every session but its owner's; a
///     replacement spawn's appearance stands for a pending one, so neither goes twice.
/// </summary>
[TestFixture]
public sealed class AppearanceTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Cloth = "item.armor.cloth";
    private const string TrailCap = "item.headgear.trail_cap";
    private const string TravelerCharm = "item.accessory.traveler_charm";

    private static List<(MessageOpcode Opcode, byte[] Payload)> Sent(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Select(message => (message.Opcode, message.Payload))
            .ToList();
    }

    private static EquipmentAppearance[] Appearances(TestServer server, ConnectionId connection)
    {
        return Sent(server, connection)
            .Where(message => message.Opcode == MessageOpcode.EquipmentAppearance)
            .Select(message =>
                EquipmentAppearance.TryRead(message.Payload, out EquipmentAppearance? read) ? read! : null!)
            .ToArray();
    }

    // Character 1 wears the sword, the cloth, the cap, and the charm as accessory 2, as earlier commits left them.
    private static ConnectionId EnterDressed(TestServer server)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        foreach ((string item, string slot) in new[]
                 {
                     (Sword, "Weapon"), (Cloth, "Armor"), (TrailCap, "Headgear"), (TravelerCharm, "Accessory2")
                 })
        {
            server.Store.GiveItems(1, item, 1, 1, 5);
            server.Store.Wear(1, server.Store.Stored(1).Items.Max(stored => stored.Id), slot);
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    // A job change's replacement spawn is followed by the appearance, which stands for the pending one (Network Protocol
    // §9): the observer hears each once.
    [Test]
    public void ReplacementSpawn_WithAnAppearancePending_SendsEachOnce()
    {
        var server = new TestServer();
        ConnectionId dressed = EnterDressed(server);
        ConnectionId observer = server.EnterWorld(2);
        server.Tick(2);
        server.Transport.ClearSent();
        PlayerEntity player = server.PlayerOf(dressed);

        player.MarkAppearanceChanged();
        player.RespawnPending = true;
        server.Tick(2);

        Assert.That(
            Sent(server, observer)
                .Where(message => message.Opcode == MessageOpcode.EntitySpawn
                    || message.Opcode == MessageOpcode.EquipmentAppearance)
                .Select(message => message.Opcode),
            Is.EqualTo(new[] { MessageOpcode.EntitySpawn, MessageOpcode.EquipmentAppearance }));
        Assert.That(Appearances(server, observer).Single().Revision, Is.EqualTo(player.AppearanceRevision));
        Assert.That((player.AppearancePending, player.RespawnPending), Is.EqualTo((false, false)));
    }

    [Test]
    public void Spawn_OfAPlayer_IsFollowedByItsAppearance_AndItsOwnerHearsNoneOfItsOwn()
    {
        var server = new TestServer();
        ConnectionId dressed = EnterDressed(server);
        ConnectionId observer = server.EnterWorld(2);
        server.Tick(2);

        EntityId seen = server.PlayerOf(dressed).Id;
        List<(MessageOpcode Opcode, byte[] Payload)> heard = Sent(server, observer);
        int spawn = heard.FindIndex(message => message.Opcode == MessageOpcode.EntitySpawn
            && EntitySpawn.TryRead(message.Payload, out EntitySpawn? read)
            && read!.Entity == seen);
        EquipmentAppearance appearance = Appearances(server, observer).Single(each => each.Entity == seen);

        Assert.That(spawn, Is.GreaterThanOrEqualTo(0), "the observer sees the dressed player");
        Assert.That(heard[spawn + 1].Opcode, Is.EqualTo(MessageOpcode.EquipmentAppearance), "right after its spawn");
        Assert.That(
            appearance.Items.Select(item => item.Value),
            Is.EqualTo(new[] { Sword, Cloth, TrailCap, "", "", "", "", TravelerCharm }));
        Assert.That(appearance.Revision, Is.EqualTo(server.PlayerOf(dressed).AppearanceRevision));
        Assert.That(
            Appearances(server, dressed).Select(each => each.Entity),
            Has.None.EqualTo(seen),
            "the owner learns what it wears from its inventory");
    }
}
}
