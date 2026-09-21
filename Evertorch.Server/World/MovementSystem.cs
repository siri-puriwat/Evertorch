using System;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
/// Moves every player one step per tick with the shared movement model. At most one input is consumed per tick,
/// so sending inputs faster than the simulation runs buys no speed.
/// </summary>
public sealed class MovementSystem : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly SessionRegistry m_sessions;
    private readonly int m_holdTicks;
    private readonly int m_maxClientTickDrift;

    public MovementSystem(
        SessionRegistry sessions,
        IOptions<WorldOptions> world,
        IOptions<SimulationOptions> simulation)
    {
        m_sessions = sessions;
        m_maxClientTickDrift = world.Value.MaxClientTickDrift;

        long holdTicks = ((long)world.Value.InputHoldTimeoutMs * simulation.Value.TickRate) / MillisecondsPerSecond;
        m_holdTicks = (int)Math.Max(1L, holdTicks);
    }

    public TickPhase Phase => TickPhase.Movement;

    public void Execute(in TickContext context)
    {
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld
                && session.Player != null
                && session.Map != null
                && session.Input != null)
            {
                Move(session.Player, session.Map, session.Input, context);
            }
        }
    }

    private void Move(PlayerEntity player, MapInstance map, PlayerInputState input, in TickContext context)
    {
        if (input.Queue.TryDequeue(out MoveIntent intent))
        {
            input.Direction = MovementModel.NormalizeOrZero(intent.DirectionX, intent.DirectionZ);
            input.LastProcessedSequence = intent.Sequence;
            input.TicksSinceInput = 0;
            TrackClientTick(input, intent.ClientTick, context.Tick);
        }
        else
        {
            // Packets get lost, so the last input keeps applying for a short while. It must not apply forever:
            // a client that vanished mid-stride would otherwise walk on until its connection timed out.
            input.TicksSinceInput++;
            if (input.TicksSinceInput > m_holdTicks)
            {
                input.Direction = new WorldDirection(0f, 0f);
            }
        }

        MovementStep step = MovementModel.Step(
            map.Definition.Navigation,
            player.Position,
            player.Facing,
            input.Direction,
            player.MovementSpeed,
            context.DeltaSeconds);

        player.Position = step.Position;
        player.Facing = step.Facing;
        player.VelocityX = step.VelocityX;
        player.VelocityY = step.VelocityY;
        player.VelocityZ = step.VelocityZ;
        player.StateFlags = step.IsMoving ? EntityStateFlags.Moving : EntityStateFlags.None;
    }

    private void TrackClientTick(PlayerInputState input, uint clientTick, uint serverTick)
    {
        uint offset = clientTick - serverTick;
        if (!input.HasClientTickOffset)
        {
            input.HasClientTickOffset = true;
            input.ClientTickOffset = offset;
            return;
        }

        int drift = (int)(offset - input.ClientTickOffset);
        if (Math.Abs((long)drift) > m_maxClientTickDrift)
        {
            input.ClientTickOffset = offset;
            input.TickDriftRebases++;
        }
    }
}
}
