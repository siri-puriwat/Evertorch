using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Players as targets (Gameplay Systems §6, §9; Network Protocol §9, §11): a live player the session knows may be
///     selected, never attacked or struck, and Mend heals its caster or the player it names, within range and in
///     sight, telling the healed player its health.
/// </summary>
[TestFixture]
public sealed class PlayerSelectionTests
{
    private const string Mend = "skill.mend";
    private const string Strike = "skill.strike";

    private delegate bool TryReader<T>(byte[] payload, out T message);

    // Two Adventurers entered side by side, each knowing the other, the first made an Arcanist with Mend 1 and its SP
    // full; nothing sent since.
    private static (TestServer Server, ConnectionId Mender, ConnectionId Other) EnterTwo(
        bool withMonsters = false,
        float interestCellSize = 16f,
        int interestNeighborRadius = 1)
    {
        var server = new TestServer(
            withMonsters: withMonsters,
            withMonsterAi: false,
            interestCellSize: interestCellSize,
            interestNeighborRadius: interestNeighborRadius);
        ConnectionId mender = server.EnterWorld(1);
        ConnectionId other = server.EnterWorld(2);
        PlayerEntity caster = server.PlayerOf(mender);
        caster.ChangeJob(new JobDefinitionId("job.arcanist"));
        caster.SetSkillLevel(new SkillDefinitionId(Mend), 1);
        caster.CurrentSpirit = caster.MaxSpirit;
        server.Tick();
        Assume.That(server.SessionOf(mender).KnownEntities, Does.Contain(server.PlayerOf(other).Id));
        server.Transport.ClearSent();
        return (server, mender, other);
    }

    // Puts the player of other distance metres from the player of from, in the first of eight directions where it can
    // stand in sight.
    private static void StandAway(TestServer server, ConnectionId from, ConnectionId other, float distance)
    {
        NavigationGrid grid = server.World.Maps.Single().Definition.Navigation;
        WorldPosition origin = server.PlayerOf(from).Position;
        for (int step = 0; step < 8; step++)
        {
            double angle = step * Math.PI / 4.0;
            float x = origin.X + distance * (float)Math.Cos(angle);
            float z = origin.Z + distance * (float)Math.Sin(angle);
            if (grid.CanOccupy(x, z)
                && grid.TrySampleHeight(x, z, out float height)
                && grid.HasLineOfSight(origin, new WorldPosition(x, height, z)))
            {
                server.PlayerOf(other).Position = new WorldPosition(x, height, z);
                return;
            }
        }

        throw new InvalidOperationException($"No place to stand {distance} m away in sight.");
    }

    private static List<T> Read<T>(TestServer server, ConnectionId player, MessageOpcode opcode, TryReader<T> read)
    {
        var messages = new List<T>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == opcode && read(message.Payload, out T found))
            {
                messages.Add(found);
            }
        }

        return messages;
    }

    private static (uint Sequence, CommandRejectionReason Reason)[] Rejections(TestServer server, ConnectionId player)
    {
        return Read(
                server,
                player,
                MessageOpcode.CommandRejected,
                (byte[] payload, out CommandRejected read) => CommandRejected.TryRead(payload, out read))
            .Select(rejected => (rejected.CommandSequence, rejected.Reason))
            .ToArray();
    }

    private static TargetChanged[] TargetChanges(TestServer server, ConnectionId player)
    {
        return Read(
                server,
                player,
                MessageOpcode.TargetChanged,
                (byte[] payload, out TargetChanged read) => TargetChanged.TryRead(payload, out read))
            .ToArray();
    }

    private static SkillResolved[] Resolutions(TestServer server, ConnectionId player)
    {
        return Read(
                server,
                player,
                MessageOpcode.SkillResolved,
                (byte[] payload, out SkillResolved read) =>
                {
                    bool isRead = SkillResolved.TryRead(payload, out SkillResolved? resolved);
                    read = resolved!;
                    return isRead;
                })
            .ToArray();
    }

    private static CharacterHealth[] Healths(TestServer server, ConnectionId player)
    {
        return Read(
                server,
                player,
                MessageOpcode.CharacterHealth,
                (byte[] payload, out CharacterHealth read) => CharacterHealth.TryRead(payload, out read))
            .ToArray();
    }

    // An ally skill with no target, or its caster's own ID, heals the caster (Gameplay Systems §9).
    [TestCase(false, TestName = "Mend_WithNoTarget_HealsTheCaster")]
    [TestCase(true, TestName = "Mend_OnItsCastersOwnId_HealsTheCaster")]
    public void Mend_OnTheCaster_HealsIt(bool isNamed)
    {
        (TestServer server, ConnectionId mender, _) = EnterTwo();
        PlayerEntity caster = server.PlayerOf(mender);
        caster.CurrentHealth = 10;

        server.SendUseSkill(mender, Mend, isNamed ? caster.Id : default, 1);
        server.Tick(30);

        SkillResolved healed = Resolutions(server, mender).Single();
        Assert.That(Rejections(server, mender), Is.Empty);
        Assert.That((healed.Caster, healed.Target, healed.Amount), Is.EqualTo((caster.Id, caster.Id, 40u)), "Mend 1");
        Assert.That(caster.CurrentHealth, Is.GreaterThanOrEqualTo(50));
    }

    // Mend reaches 6 m and the 0.5 m tolerance (Gameplay Systems §9).
    [TestCase(6.4f, CommandRejectionReason.None, TestName = "Mend_OnAPlayerWithinItsRangeAndTolerance_Begins")]
    [TestCase(6.8f, CommandRejectionReason.OutOfRange, TestName = "Mend_OnAPlayerBeyondItsRange_IsRefusedOutOfRange")]
    public void Mend_OnAPlayerAtADistance(float distance, CommandRejectionReason expected)
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        StandAway(server, mender, other, distance);
        server.Tick();
        server.Transport.ClearSent();

        server.SendUseSkill(mender, Mend, server.PlayerOf(other).Id, 1);
        server.Tick();

        (uint, CommandRejectionReason)[] refusals = expected == CommandRejectionReason.None
            ? Array.Empty<(uint, CommandRejectionReason)>()
            : new[] { (1u, expected) };
        Assert.That(Rejections(server, mender), Is.EqualTo(refusals));
        Assert.That(server.PlayerOf(mender).Combat.IsCasting, Is.EqualTo(expected == CommandRejectionReason.None));
    }

    [Test]
    public void Attack_OnAPlayer_IsRefusedAndNeverSelectsOrSwings()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        PlayerEntity target = server.PlayerOf(other);

        server.SendAttack(mender, target.Id, 1);
        server.Tick(40);

        Assert.That(Rejections(server, mender), Is.EqualTo(new[] { (1u, CommandRejectionReason.InvalidTarget) }));
        Assert.That(server.PlayerOf(mender).Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.Transport.ControlOpcodesSentTo(mender), Does.Not.Contain(MessageOpcode.AttackStarted));
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
    }

    [Test]
    public void EnemySkill_OnAPlayer_IsRefused()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        PlayerEntity target = server.PlayerOf(mender);
        server.PlayerOf(other).CurrentSpirit = server.PlayerOf(other).MaxSpirit;
        server.SendTarget(other, target.Id);

        server.SendUseSkill(other, Strike, target.Id, 1);
        server.Tick(40);

        Assert.That(TargetChanges(server, other).Single().Target, Is.EqualTo(target.Id), "selected");
        Assert.That(Rejections(server, other), Is.EqualTo(new[] { (1u, CommandRejectionReason.InvalidTarget) }));
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
    }

    [Test]
    public void Mend_OnAMonster_IsRefused()
    {
        (TestServer server, ConnectionId mender, _) = EnterTwo(true);
        PlayerEntity caster = server.PlayerOf(mender);
        MonsterEntity monster = server.MonstersNear(caster.Position).First();
        server.PlayerOf(mender).Position = monster.Position;
        server.Tick();
        server.Transport.ClearSent();

        server.SendUseSkill(mender, Mend, monster.Id, 1);
        server.Tick(30);

        Assert.That(Rejections(server, mender), Is.EqualTo(new[] { (1u, CommandRejectionReason.InvalidTarget) }));
        Assert.That(caster.CurrentSpirit, Is.EqualTo(caster.MaxSpirit), "nothing paid");
    }

    [Test]
    public void Mend_OnAPlayerBehindAWall_IsRefusedOutOfRange()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        NavigationGrid grid = server.World.Maps.Single().Definition.Navigation;
        WorldPosition? west = null;
        WorldPosition? east = null;
        for (int row = 0; row < grid.Rows && west == null; row++)
        {
            for (int column = 1; column < grid.Columns - 1 && west == null; column++)
            {
                WorldPosition wall = grid.GetCellCenter(column, row);
                if (grid.GetCell(column, row).Surface == NavigationSurface.Wall
                    && grid.CanOccupy(wall.X - 0.95f, wall.Z)
                    && grid.CanOccupy(wall.X + 0.95f, wall.Z))
                {
                    grid.TrySampleHeight(wall.X - 0.95f, wall.Z, out float westHeight);
                    grid.TrySampleHeight(wall.X + 0.95f, wall.Z, out float eastHeight);
                    west = new WorldPosition(wall.X - 0.95f, westHeight, wall.Z);
                    east = new WorldPosition(wall.X + 0.95f, eastHeight, wall.Z);
                }
            }
        }

        Assert.That(west, Is.Not.Null, "the training ground has a one-cell wall");
        server.PlayerOf(mender).Position = west!.Value;
        server.PlayerOf(other).Position = east!.Value;
        server.Tick();
        Assume.That(server.SessionOf(mender).KnownEntities, Does.Contain(server.PlayerOf(other).Id));
        server.Transport.ClearSent();

        server.SendUseSkill(mender, Mend, server.PlayerOf(other).Id, 1);
        server.Tick();

        Assert.That(Rejections(server, mender), Is.EqualTo(new[] { (1u, CommandRejectionReason.OutOfRange) }));
    }

    // A dead player, an unknown one, and one out of view cannot be named, as a missing one cannot.
    [Test]
    public void Mend_OnAPlayerItCannotName_IsRefused()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        PlayerEntity target = server.PlayerOf(other);
        server.Combat.Kill(server.World.Maps.Single(), target, null, server.CurrentTick);
        server.Tick();
        server.Transport.ClearSent();

        server.SendUseSkill(mender, Mend, target.Id, 1);
        server.SendUseSkill(mender, Mend, new EntityId(9999), 2);
        server.Tick();

        Assert.That(
            Rejections(server, mender),
            Is.EqualTo(new[]
                { (1u, CommandRejectionReason.InvalidTarget), (2u, CommandRejectionReason.InvalidTarget) }));
    }

    // Mend on the selected player heals it: the cast names it to those who know the caster, the heal reaches those
    // who know it, and the healed player alone is sent its health; the caster pays and keeps its own HP.
    [Test]
    public void Mend_OnAnotherPlayer_HealsItAndTellsItsHealth()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        PlayerEntity caster = server.PlayerOf(mender);
        PlayerEntity target = server.PlayerOf(other);
        caster.CurrentHealth = 10;
        target.CurrentHealth = 10;
        int spirit = caster.CurrentSpirit;
        server.SendTarget(mender, target.Id);
        server.Tick();

        server.SendUseSkill(mender, Mend, target.Id, 1);
        server.Tick(30);

        Assert.That(Rejections(server, mender), Is.Empty);
        Assert.That(TargetChanges(server, mender).Single().Target, Is.EqualTo(target.Id), "selected first");
        SkillCastStarted cast = Read(
                server,
                other,
                MessageOpcode.SkillCastStarted,
                (byte[] payload, out SkillCastStarted read) =>
                {
                    bool isRead = SkillCastStarted.TryRead(payload, out SkillCastStarted? started);
                    read = started!;
                    return isRead;
                })
            .Single();
        Assert.That((cast.Caster, cast.Target), Is.EqualTo((caster.Id, target.Id)), "the healed sees the cast at it");
        foreach (ConnectionId seeing in new[] { mender, other })
        {
            SkillResolved healed = Resolutions(server, seeing).Single();
            Assert.That(
                (healed.Caster, healed.Target, healed.Outcome, healed.Amount),
                Is.EqualTo((caster.Id, target.Id, SkillOutcome.Healed, 40u)),
                "Mend 1 on the other");
        }

        Assert.That(target.CurrentHealth, Is.GreaterThanOrEqualTo(50));
        Assert.That(Healths(server, other).Last().Current, Is.EqualTo((uint)target.CurrentHealth));
        Assert.That(caster.CurrentHealth, Is.LessThan(50), "the caster not healed");
        Assert.That(caster.CurrentSpirit, Is.LessThan(spirit), "the caster paid");
    }

    [Test]
    public void Target_ForAPlayerItKnows_IsConfirmedAndStartsNoAttack()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        PlayerEntity target = server.PlayerOf(other);

        server.SendTarget(mender, target.Id);
        server.Tick(40);

        TargetChanged changed = TargetChanges(server, mender).Single();
        Assert.That((changed.Actor, changed.Target), Is.EqualTo((server.PlayerOf(mender).Id, target.Id)));
        Assert.That(server.PlayerOf(mender).Combat.IsAutoAttacking, Is.False);
        Assert.That(server.Transport.ControlOpcodesSentTo(mender), Does.Not.Contain(MessageOpcode.AttackStarted));
        Assert.That(TargetChanges(server, other), Is.Empty, "only the owner hears of it");
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
    }

    // Itself, a dead player, and one out of view cannot be selected; each refusal is counted (Network Protocol §11).
    [Test]
    public void Target_ForItselfADeadOrAnUnseenPlayer_IsRefused()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo(
            interestCellSize: 4f,
            interestNeighborRadius: 0);
        PlayerEntity target = server.PlayerOf(other);

        server.SendTarget(mender, server.PlayerOf(mender).Id);
        StandAway(server, mender, other, 12f);
        server.Tick();
        Assume.That(server.SessionOf(mender).KnownEntities, Does.Not.Contain(target.Id));
        server.SendTarget(mender, target.Id);
        server.Tick();
        server.PlayerOf(other).Position = server.PlayerOf(mender).Position;
        server.Tick();
        server.Combat.Kill(server.World.Maps.Single(), target, null, server.CurrentTick);
        server.SendTarget(mender, target.Id);
        server.Tick();

        Assert.That(server.PlayerOf(mender).Target, Is.EqualTo(default(EntityId)));
        Assert.That(TargetChanges(server, mender), Is.Empty);
        Assert.That(server.SessionOf(mender).RefusedCommands, Is.EqualTo(3));
    }

    // The selected player's death clears the selection, as a monster's does (Gameplay Systems §6).
    [Test]
    public void Target_WhenTheSelectedPlayerDies_IsCleared()
    {
        (TestServer server, ConnectionId mender, ConnectionId other) = EnterTwo();
        PlayerEntity target = server.PlayerOf(other);
        server.SendTarget(mender, target.Id);
        server.Tick();
        server.Transport.ClearSent();

        server.Combat.Kill(server.World.Maps.Single(), target, null, server.CurrentTick);
        server.Tick();

        Assert.That(TargetChanges(server, mender).Single().Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.PlayerOf(mender).Target, Is.EqualTo(default(EntityId)));
    }
}
}
