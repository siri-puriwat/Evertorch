using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     One client simulation tick for the local player: pick a direction, turn it into an intent, predict it, send it.
/// </summary>
public sealed class LocalPlayerDriver
{
    private readonly MovementController m_controller;
    private readonly MoveIntentProducer m_producer;
    private readonly ClientWorld m_world;
    private readonly IMoveIntentSink m_sink;
    private bool m_wasMoving;
    private bool m_isStopOutstanding;
    private uint m_firstStopSequence;

    public LocalPlayerDriver(
        MovementController controller,
        MoveIntentProducer producer,
        ClientWorld world,
        IMoveIntentSink sink)
    {
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
        m_producer = producer ?? throw new ArgumentNullException(nameof(producer));
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public int IntentsSent { get; private set; }

    public void Tick(uint clientTick)
    {
        MovementPredictor predictor = m_world.Predictor;
        WorldPosition previous = predictor.Position;
        WorldDirection direction = m_controller.Tick(previous, predictor.StepDistance);
        bool isIdle = direction == default;

        if (isIdle && !NeedsStop(predictor))
        {
            m_world.Smoother.OnTick(previous, previous);
            return;
        }

        MoveIntent intent = m_producer.Next(clientTick, direction);
        if (isIdle && m_wasMoving)
        {
            m_isStopOutstanding = true;
            m_firstStopSequence = intent.Sequence;
        }
        else if (!isIdle)
        {
            m_isStopOutstanding = false;
        }

        predictor.Apply(intent);
        m_sink.Send(intent);
        IntentsSent++;
        m_wasMoving = !isIdle;
        m_world.Smoother.OnTick(previous, predictor.Position);
    }

    // Input travels unreliably, so a stop is repeated, with a new sequence each tick, until the server acknowledges
    // that stop or a later one. After that an idle player sends nothing.
    private bool NeedsStop(MovementPredictor predictor)
    {
        if (m_wasMoving)
        {
            return true;
        }

        if (!m_isStopOutstanding)
        {
            return false;
        }

        bool isAcknowledged = unchecked((int)(predictor.LastAcknowledgedSequence - m_firstStopSequence)) >= 0;
        if (isAcknowledged)
        {
            predictor.ForgetPendingStops();
        }

        m_isStopOutstanding = !isAcknowledged;
        return m_isStopOutstanding;
    }
}
}
