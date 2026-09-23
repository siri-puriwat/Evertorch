using System.Linq;
using Evertorch.Game;
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
