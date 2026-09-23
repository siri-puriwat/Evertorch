using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The client's real approach, lock, and cancel code against the server's real attack loop, over a link with
///     latency (Gameplay Systems §5.1, §6).
/// </summary>
[TestFixture]
public sealed class ClientApproachTests
{
    private const int LimitMilliseconds = 20000;

    private static (ClientServerRig Rig, SimulatedClient Client, MonsterEntity Slime) Connect()
    {
        var rig = new ClientServerRig(true, new SureHitRandom());
        SimulatedClient client = rig.AddClient(7, 1, 17);
        client.Link.LatencyMilliseconds = 50;
        rig.ConnectAll();
        MonsterEntity slime = rig.Server.MonstersNear(rig.Server.World.Maps.Single().Definition.SpawnPosition).First();
        slime.CurrentHealth = 1_000_000;
        rig.AdvanceUntil(() => client.Connection.World != null && client.World.Remotes.ContainsKey(slime.Id), 3000);
        return (rig, client, slime);
    }

    [TestCase(50)]
    [TestCase(100)]
    public void Attack_OnARoamingSlime_KillsIt(int oneWayLatencyMs)
    {
        var rig = new ClientServerRig(true, new SureHitRandom());
        SimulatedClient client = rig.AddClient(7, 3, 17);
        client.Link.LatencyMilliseconds = oneWayLatencyMs;
        rig.ConnectAll();
        MapInstance map = rig.Server.World.Maps.Single();
        MonsterEntity slime = rig.Server.MonstersNear(map.Definition.SpawnPosition).First();
        rig.AdvanceUntil(() => client.Connection.World != null && client.World.Remotes.ContainsKey(slime.Id), 3000);
        rig.AdvanceUntil(() => slime.Brain.State == MonsterAiState.Roam, LimitMilliseconds);
        Assume.That(slime.Brain.State, Is.EqualTo(MonsterAiState.Roam), "attacked while it walks");
        var deaths = new List<EntityId>();
        client.World.EntityDiedReceived += died => deaths.Add(died.Entity);

        client.AutoAttack!.Attack(slime.Id);
        int took = rig.AdvanceUntil(() => deaths.Contains(slime.Id), 30000);

        Assert.That(took, Is.GreaterThan(0), "four hits of 13 kill the 50 HP slime");
        Assert.That(client.AutoAttack.CancelsSent, Is.Zero);
        Assert.That(client.World.Smoother.Snaps, Is.Zero);
    }

    [Test]
    public void Attack_FromAcrossTheMap_WalksIntoRangeAndIsHitWithoutACorrection()
    {
        (ClientServerRig rig, SimulatedClient client, MonsterEntity slime) = Connect();
        int damages = 0;
        client.World.DamageReceived += _ => damages++;
        client.AutoAttack!.Attack(slime.Id);

        int took = rig.AdvanceUntil(() => damages >= 2, LimitMilliseconds);

        Assert.That(took, Is.GreaterThan(0), "the approach ended in range and two swings landed");
        Assert.That(client.AutoAttack.CancelsSent, Is.Zero, "approach steps never cancel");
        Assert.That(client.World.Smoother.Snaps, Is.Zero);
        Assert.That(client.World.Smoother.LargestCorrection, Is.LessThan(1e-3f));
        Assert.That(rig.Server.SessionOf(rig.Server.Sessions.Sessions.Single().Connection).RefusedCommands, Is.Zero);
    }

    [Test]
    public void Attack_ThatTheServerRefuses_EndsTheClientsChase()
    {
        var rig = new ClientServerRig();
        SimulatedClient attacker = rig.AddClient(7, 1, 17);
        SimulatedClient other = rig.AddClient(8, 2, 29);
        rig.ConnectAll();
        rig.AdvanceUntil(() => attacker.Connection.World != null && other.Connection.World != null, 3000);
        EntityId otherEntity = other.World.LocalEntity;
        rig.AdvanceUntil(() => attacker.World.Remotes.ContainsKey(otherEntity), 3000);
        var rejections = new List<CommandRejected>();
        attacker.World.CommandRejectedReceived += rejections.Add;

        // Players are not targetable in version 1; before CommandRejected the client chased such a target forever.
        attacker.AutoAttack!.Attack(otherEntity);
        int took = rig.AdvanceUntil(() => !attacker.AutoAttack.IsActive, 3000);

        Assert.That(took, Is.GreaterThan(0), "the chase ended");
        Assert.That(rejections.Single().Reason, Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(attacker.AutoAttack.CancelsSent, Is.Zero);
    }

    [Test]
    public void Attack_UntilTheSlimeDies_ShowsTheClientTheSlimeGelItDropped()
    {
        // The slime's one drop entry rolls 0, below any chance, and draws the smallest amount.
        var rig = new ClientServerRig(true, new SureHitRandom(), new ScriptedRandom(0));
        SimulatedClient client = rig.AddClient(7, 3, 17);
        client.Link.LatencyMilliseconds = 50;
        rig.ConnectAll();
        MapInstance map = rig.Server.World.Maps.Single();
        MonsterEntity slime = rig.Server.MonstersNear(map.Definition.SpawnPosition).First();
        rig.AdvanceUntil(() => client.Connection.World != null && client.World.Remotes.ContainsKey(slime.Id), 3000);
        var dropped = new List<ItemDropped>();
        client.World.ItemDroppedReceived += dropped.Add;

        client.AutoAttack!.Attack(slime.Id);
        int took = rig.AdvanceUntil(() => dropped.Count > 0, 30000);

        Assert.That(took, Is.GreaterThan(0), "the slime died and dropped");
        ItemDropped gel = dropped.Single();
        Assert.That(gel.ItemId, Is.EqualTo("item.material.slime_gel"));
        Assert.That(gel.Amount, Is.EqualTo(1u));
        Assert.That(client.World.Remotes[gel.Entity].Kind, Is.EqualTo(EntityKind.ItemDrop));
        Assert.That(client.World.Remotes[gel.Entity].DefinitionId, Is.EqualTo("item.material.slime_gel"));
        Assert.That(map.ItemDrops.Single().Id, Is.EqualTo(gel.Entity));
        Assert.That(client.World.Remotes[slime.Id].IsDead, Is.True);
        Assert.That(client.AutoAttack.CancelsSent, Is.Zero);
    }

    [Test]
    public void Walk_RequestedDuringTheOwnSwing_StartsAtImpactWithoutACorrection()
    {
        (ClientServerRig rig, SimulatedClient client, MonsterEntity slime) = Connect();
        int starts = 0;
        client.World.AttackStartedReceived += started => starts += started.Attacker == client.World.LocalEntity ? 1 : 0;
        client.AutoAttack!.Attack(slime.Id);
        Assert.That(rig.AdvanceUntil(() => starts >= 1, LimitMilliseconds), Is.GreaterThan(0));
        WorldPosition atSwing = client.World.Predictor.Position;
        WorldPosition away = rig.Server.World.Maps.Single().Definition.SpawnPosition;

        Assert.That(client.Controller!.TryMoveTo(atSwing, away), Is.True);
        client.AutoAttack.OnWalkRequested();
        rig.Advance(300);
        WorldPosition duringLock = client.World.Predictor.Position;
        rig.Advance(1500);

        Assert.That(duringLock, Is.EqualTo(atSwing), "the predictor holds, as the server does");
        Assert.That(client.World.Predictor.Position, Is.Not.EqualTo(atSwing), "the walk starts after the impact");
        Assert.That(client.AutoAttack.CancelsSent, Is.EqualTo(1));
        Assert.That(client.World.Smoother.Snaps, Is.Zero);
        Assert.That(client.World.Smoother.LargestCorrection, Is.LessThan(1e-3f));
    }
}
}
