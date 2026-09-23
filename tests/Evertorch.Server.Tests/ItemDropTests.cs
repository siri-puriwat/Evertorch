using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class ItemDropTests
{
    private const string SlimeGel = "item.material.slime_gel";

    // North row first, 1 m cells from the origin.
    private static readonly string[] OpenField = CreateOpenField(16);

    private static string[] CreateOpenField(int size)
    {
        string[] rows = new string[size];
        for (int row = 0; row < size; row++)
        {
            rows[row] = row == 0 || row == size - 1
                ? new string('#', size)
                : "#" + new string('.', size - 2) + "#";
        }

        return rows;
    }

    private static NavigationGrid CreateGrid(string[] northFirstRows)
    {
        int rows = northFirstRows.Length;
        int columns = northFirstRows[0].Length;
        var cells = new NavigationCell[rows * columns];
        for (int row = 0; row < rows; row++)
        {
            string text = northFirstRows[rows - 1 - row];
            for (int column = 0; column < columns; column++)
            {
                NavigationSurface surface = text[column] == '#' ? NavigationSurface.Wall : NavigationSurface.Floor;
                cells[row * columns + column] = NavigationCell.Level(surface, 0f);
            }
        }

        return new NavigationGrid(columns, rows, 1f, 0f, 0f, 0.3f, 0.4f, cells);
    }

    // The slime's table is one entry: slime gel, chance 0.70, amount 1–2. The drop draws are its roll, then its
    // amount; anything drawn after that falls back to a roll that never drops.
    private static CombatRig KillTheSlime(params int[] dropDraws)
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom(), dropRandom: new ScriptedRandom(999_999, dropDraws));
        rig.Slime.CurrentHealth = 1;
        rig.Attack(rig.Slime.Id);
        for (int index = 0; index < 200 && !rig.Slime.IsDead; index++)
        {
            rig.Server.Tick();
        }

        Assert.That(rig.Slime.IsDead, Is.True);
        return rig;
    }

    private static ItemDropped ReadDropped(InMemoryServerTransport.SentMessage message)
    {
        Assert.That(ItemDropped.TryRead(message.Payload, out ItemDropped? dropped), Is.True);
        return dropped!;
    }

    [TestCase(699_999, 0.70, true)]
    [TestCase(700_000, 0.70, false)]
    [TestCase(0, 0.0, false)]
    [TestCase(999_999, 1.0, true)]
    public void IsDropped_ComparesTheDrawBelowTheChanceInMillionths(int draw, double chance, bool expected)
    {
        Assert.That(ItemDropSystem.IsDropped(draw, chance), Is.EqualTo(expected));
    }

    [Test]
    public void Death_WithADrawAtTheChance_DropsNothingAndDrawsNoAmount()
    {
        var random = new ScriptedRandom(0, 700_000);
        var rig = new CombatRig(combatRandom: new SureHitRandom(), dropRandom: random);
        rig.Slime.CurrentHealth = 1;
        rig.Attack(rig.Slime.Id);
        for (int index = 0; index < 200 && !rig.Slime.IsDead; index++)
        {
            rig.Server.Tick();
        }

        Assert.That(rig.Slime.IsDead, Is.True);
        Assert.That(rig.Map.ItemDrops, Is.Empty);
        Assert.That(random.Calls, Is.EqualTo(1));
        Assert.That(rig.Server.Transport.ControlOpcodesSentTo(rig.Player), Has.No.Member(MessageOpcode.ItemDropped));
    }

    [Test]
    public void Death_WithADrawBelowTheChance_DropsTheRolledAmountOnTheBodyAndTellsTheWatcher()
    {
        CombatRig rig = KillTheSlime(699_999, 1);

        ItemDropEntity drop = rig.Map.ItemDrops.Single();
        Assert.That(drop.DefinitionId, Is.EqualTo(SlimeGel));
        Assert.That(drop.Amount, Is.EqualTo(2u));
        Assert.That(drop.Position, Is.EqualTo(rig.Slime.Position));

        var sent = rig.Server.Transport.ControlSentTo(rig.Player).ToList();
        var opcodes = sent.Select(message => message.Opcode).ToList();
        int died = opcodes.IndexOf(MessageOpcode.EntityDied);
        int dropped = opcodes.IndexOf(MessageOpcode.ItemDropped);
        Assert.That(died, Is.GreaterThanOrEqualTo(0));
        Assert.That(dropped, Is.GreaterThan(died));
        Assert.That(opcodes[dropped - 1], Is.EqualTo(MessageOpcode.EntitySpawn), "the spawn comes first");
        Assert.That(EntitySpawn.TryRead(sent[dropped - 1].Payload, out EntitySpawn? spawn), Is.True);
        Assert.That(spawn!.Kind, Is.EqualTo(EntityKind.ItemDrop));
        Assert.That(spawn.DefinitionId, Is.EqualTo(SlimeGel));
        ItemDropped message = ReadDropped(sent[dropped]);
        Assert.That(message.Entity, Is.EqualTo(drop.Id));
        Assert.That(message.ItemId, Is.EqualTo(SlimeGel));
        Assert.That(message.Amount, Is.EqualTo(2u));
        Assert.That(message.Position, Is.EqualTo(drop.Position));
    }

    [Test]
    public void Drop_AtTheEndOfItsLifetime_DespawnsAsRemoved()
    {
        CombatRig rig = KillTheSlime(0, 0);
        ItemDropEntity drop = rig.Map.ItemDrops.Single();
        uint lastTick = drop.DroppedTick + 60000 / 50 - 1;

        rig.Server.Tick((int)(lastTick - rig.Server.CurrentTick));
        bool isThereOnItsLastTick = rig.Map.Contains(drop.Id);
        rig.Server.Transport.ClearSent();
        rig.Server.Tick();

        Assert.That(isThereOnItsLastTick, Is.True);
        Assert.That(rig.Map.Contains(drop.Id), Is.False);
        InMemoryServerTransport.SentMessage despawn = rig.Server.Transport.ControlSentTo(rig.Player)
            .Single(message => message.Opcode == MessageOpcode.EntityDespawn);
        Assert.That(EntityDespawn.TryRead(despawn.Payload, out EntityDespawn read), Is.True);
        Assert.That(read.Entity, Is.EqualTo(drop.Id));
        Assert.That(read.Reason, Is.EqualTo(DespawnReason.Removed));
    }

    [Test]
    public void Drop_IsNeverInASnapshotAndCannotBeTargeted()
    {
        CombatRig rig = KillTheSlime(0, 0);
        ItemDropEntity drop = rig.Map.ItemDrops.Single();
        rig.Server.Transport.ClearSent();

        rig.Server.SendTarget(rig.Player, drop.Id);
        rig.Server.Tick(3);

        EntitySnapshot[] snapshots = rig.Server.Transport.SnapshotsSentTo(rig.Player).ToArray();
        Assert.That(snapshots, Is.Not.Empty);
        EntityId[] snapshotted = snapshots.SelectMany(snapshot => snapshot.Entities).Select(state => state.Entity)
            .ToArray();
        Assert.That(snapshotted, Has.No.Member(drop.Id));
        Assert.That(rig.Server.SessionOf(rig.Player).RefusedCommands, Is.EqualTo(1));
        Assert.That(rig.Entity.Target, Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void Drop_SeenLater_ArrivesAsASpawnWithoutItsAmount()
    {
        CombatRig rig = KillTheSlime(0, 0);
        ItemDropEntity drop = rig.Map.ItemDrops.Single();

        ConnectionId late = rig.Server.EnterWorld(2);
        rig.Server.Place(late, drop.Position.X, drop.Position.Z);
        rig.Server.Tick();

        IReadOnlyList<InMemoryServerTransport.SentMessage> sent = rig.Server.Transport.ControlSentTo(late);
        Assert.That(sent.Select(message => message.Opcode), Has.No.Member(MessageOpcode.ItemDropped));
        Assert.That(
            sent.Where(message => message.Opcode == MessageOpcode.EntitySpawn)
                .Select(message =>
                    EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn) ? spawn!.Entity : default),
            Has.Member(drop.Id));
    }

    [Test]
    public void Place_OnOpenFloor_PutsTheFirstOnTheBodyThenSouthEastWestNorth()
    {
        NavigationGrid grid = CreateGrid(OpenField);
        var body = new WorldPosition(12.3f, 0f, 12.7f);

        WorldPosition[] placed = Enumerable.Range(0, 5).Select(index => ItemDropSystem.Place(grid, body, index))
            .ToArray();

        Assert.That(placed[0], Is.EqualTo(body));
        Assert.That(placed[1], Is.EqualTo(new WorldPosition(13.3f, 0f, 11.7f)));
        Assert.That(placed[2], Is.EqualTo(new WorldPosition(11.3f, 0f, 12.7f)));
        Assert.That(placed[3], Is.EqualTo(new WorldPosition(12.3f, 0f, 13.7f)));
        Assert.That(placed[4], Is.EqualTo(placed[1]), "the offsets go around again");
    }

    [Test]
    public void Place_WhereAnAgentCannotStand_FallsBackToTheBody()
    {
        NavigationGrid grid = CreateGrid(OpenField);
        var body = new WorldPosition(1.5f, 0f, 1.5f);

        Assert.That(ItemDropSystem.Place(grid, body, 1), Is.EqualTo(body), "south-east is the wall");
        Assert.That(ItemDropSystem.Place(grid, body, 3), Is.EqualTo(new WorldPosition(1.5f, 0f, 2.5f)));
    }
}
}
