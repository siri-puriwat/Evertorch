using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A player's name (Milestone 12 line 3): it comes with the character's load, reaches those who see the player in
///     its spawn, and finds a reachable character whatever its case (Network Protocol §6; Gameplay Systems §15).
/// </summary>
[TestFixture]
public sealed class CharacterNameTests
{
    private static List<EntitySpawn> SpawnsSentTo(TestServer server, ConnectionId connection)
    {
        return server.Transport.SentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.EntitySpawn)
            .Select(message => EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn) ? spawn! : null!)
            .ToList();
    }

    [Test]
    public void Player_Entering_HasTheNameItWasStoredWith()
    {
        var server = new TestServer();

        ConnectionId connection = server.EnterWorld(7);

        Assert.That(server.PlayerOf(connection).Name, Is.EqualTo("Tester7"));
    }

    [Test]
    public void Spawn_OfAnotherPlayer_NamesIt_AndNoOtherKindIsNamed()
    {
        var server = new TestServer(withMonsters: true, withNpcs: true);
        ConnectionId first = server.EnterWorld(7);
        ConnectionId second = server.EnterWorld(8);
        server.Tick(2);

        List<EntitySpawn> spawns = SpawnsSentTo(server, second);

        Assert.That(spawns, Has.None.Null, "every spawn reads back");
        EntitySpawn seen = spawns.Single(spawn => spawn.Entity == server.PlayerOf(first).Id);
        Assert.That(seen.Name, Is.EqualTo("Tester7"));
        Assert.That(spawns.Where(spawn => spawn.Kind != EntityKind.Player), Is.Not.Empty);
        Assert.That(spawns.Where(spawn => spawn.Kind != EntityKind.Player).Select(spawn => spawn.Name),
            Has.All.Empty);
    }

    [Test]
    public void TryGetReachable_AfterTheCharacterLeftTheWorld_IsFalse()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);

        server.Disconnect(connection);
        server.TickUntil(() => !server.Sessions.TryGetCharacter(new CharacterId(7), out _));

        Assert.That(server.Sessions.TryGetReachable("Tester7", out _), Is.False);
    }

    [Test]
    public void TryGetReachable_FindsACharacterInTheWorld_WhateverItsCase()
    {
        var server = new TestServer();
        server.EnterWorld(7);

        bool isFound = server.Sessions.TryGetReachable("tESTER7", out CharacterSession? found);

        Assert.That(isFound, Is.True);
        Assert.That(found!.Character, Is.EqualTo(new CharacterId(7)));
        Assert.That(server.Sessions.TryGetReachable("Tester8", out _), Is.False, "nobody by that name");
    }

    [Test]
    public void TryGetReachable_InTheReconnectGrace_IsFalse_AndAfterAReconnect_IsTrueAgain()
    {
        var server = new TestServer(reconnectGraceMs: 30_000);
        ConnectionId connection = server.EnterWorld(7);

        server.Disconnect(connection);
        server.TickUntil(() => !server.Sessions.TryGetReachable("Tester7", out _));

        Assert.That(server.Sessions.TryGetCharacter(new CharacterId(7), out _), Is.True, "still in the world");
        server.EnterWorld(7);
        Assert.That(server.Sessions.TryGetReachable("Tester7", out _), Is.True, "reattached");
    }
}
}
