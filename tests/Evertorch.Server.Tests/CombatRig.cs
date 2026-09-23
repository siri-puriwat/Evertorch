using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A player standing next to a training slime on the repository map, with helpers to read the combat events
///     the server sent it.
/// </summary>
internal sealed class CombatRig
{
    private uint m_commandSequence;
    private uint m_inputSequence;

    public CombatRig(float distance = 1.2f, IRandomSource? combatRandom = null)
    {
        Server = new TestServer(withMonsters: true, combatRandom: combatRandom);
        Player = Server.EnterWorld(1);
        Map = Server.World.Maps.Single();
        Slime = Server.MonstersNear(Map.Definition.SpawnPosition).First();
        StandBeside(Slime, distance);
        Server.Tick();
        Server.Transport.ClearSent();
    }

    public TestServer Server { get; }

    public ConnectionId Player { get; }

    public MapInstance Map { get; }

    public MonsterEntity Slime { get; }

    public PlayerEntity Entity => Server.PlayerOf(Player);

    /// <summary>
    ///     Puts the player <paramref name="distance" /> from <paramref name="monster" />, in the first of eight
    ///     directions where it can stand and see the monster.
    /// </summary>
    public void StandBeside(MonsterEntity monster, float distance)
    {
        NavigationGrid grid = Map.Definition.Navigation;
        for (int step = 0; step < 8; step++)
        {
            double angle = step * Math.PI / 4.0;
            float x = monster.Position.X + distance * (float)Math.Cos(angle);
            float z = monster.Position.Z + distance * (float)Math.Sin(angle);
            if (grid.CanOccupy(x, z)
                && grid.TrySampleHeight(x, z, out float height)
                && grid.HasLineOfSight(new WorldPosition(x, height, z), monster.Position))
            {
                Entity.Position = new WorldPosition(x, height, z);
                return;
            }
        }

        throw new InvalidOperationException("No place to stand beside the monster.");
    }

    public void Attack(EntityId target)
    {
        Server.SendAttack(Player, target, ++m_commandSequence);
    }

    public void Cancel()
    {
        Server.SendCancel(Player, ++m_commandSequence);
    }

    public void Move(float directionX, float directionZ)
    {
        m_inputSequence++;
        Server.SendMove(Player, m_inputSequence, directionX, directionZ);
    }

    public List<T> Received<T>(MessageOpcode opcode, Func<byte[], T?> read)
        where T : struct
    {
        var messages = new List<T>();
        foreach (InMemoryServerTransport.SentMessage message in Server.Transport.ControlSentTo(Player))
        {
            T? decoded = message.Opcode == opcode ? read(message.Payload) : null;
            if (decoded.HasValue)
            {
                messages.Add(decoded.Value);
            }
        }

        return messages;
    }

    public List<AttackStarted> Starts()
    {
        return Received(
            MessageOpcode.AttackStarted,
            payload => AttackStarted.TryRead(payload, out AttackStarted m) ? m : (AttackStarted?)null);
    }

    public List<Damage> Damages()
    {
        return Received(MessageOpcode.Damage, payload => Damage.TryRead(payload, out Damage m) ? m : (Damage?)null);
    }

    public List<EntityDied> Deaths()
    {
        return Received(
            MessageOpcode.EntityDied,
            payload => EntityDied.TryRead(payload, out EntityDied m) ? m : (EntityDied?)null);
    }

    /// <summary>
    ///     Ticks until the player has been told of <paramref name="count" /> swings, or the limit passes.
    /// </summary>
    public void TickUntilStarts(int count, int limit = 2000)
    {
        for (int index = 0; index < limit && Starts().Count < count; index++)
        {
            Server.Tick();
        }
    }
}

/// <summary>
///     Always hits, never dodges or crits, and rolls the lowest variance: every bound of 1000 (the dodge and critical
///     rolls) draws 999, everything else 0.
/// </summary>
internal sealed class SureHitRandom : IRandomSource
{
    public int Next(int exclusiveMax)
    {
        return exclusiveMax == 1000 ? 999 : 0;
    }
}
}
