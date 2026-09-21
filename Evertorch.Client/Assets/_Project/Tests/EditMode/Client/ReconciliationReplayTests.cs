using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
/// The whole local-player loop (controller, intent producer, predictor, smoother) against a miniature server, with
/// the link between them misbehaving in a scripted way.
/// </summary>
[TestFixture]
public sealed class ReconciliationReplayTests
{
    private const int MoveTicks = 40;
    private const int SettleTicks = 40;
    private const int QuietTicks = 10;

    private static readonly WorldPosition Start = ClientTestGrids.Center(2, 8);

    [Test]
    public void PerfectLink_PredictionNeverNeedsCorrecting()
    {
        Scenario scenario = new Scenario(0, 0, (tick, isUplink) => true);

        scenario.Run();

        Assert.That(scenario.World.Smoother.LargestCorrection, Is.LessThan(1e-4f));
        scenario.AssertConverged();
    }

    [Test]
    public void DelayedSnapshots_AreReplayedOverWithoutVisibleError()
    {
        Scenario scenario = new Scenario(4, 4, (tick, isUplink) => true);

        scenario.Run();

        Assert.That(scenario.World.Smoother.LargestCorrection, Is.LessThan(1e-4f));
        Assert.That(scenario.LargestPendingCount, Is.GreaterThan(4), "inputs were really waiting for their ack");
        scenario.AssertConverged();
    }

    [Test]
    public void MissingSnapshots_DoNotDisturbThePrediction()
    {
        Scenario scenario = new Scenario(2, 2, (tick, isUplink) => isUplink || tick % 3 != 0);

        scenario.Run();

        Assert.That(scenario.World.Smoother.LargestCorrection, Is.LessThan(1e-4f));
        scenario.AssertConverged();
    }

    [Test]
    public void LostInputs_CauseSmallCorrectionsAndStillConverge()
    {
        Scenario scenario = new Scenario(2, 2, (tick, isUplink) => !isUplink || tick % 4 != 0);

        scenario.Run();

        Assert.That(scenario.World.Smoother.LargestCorrection, Is.GreaterThan(0f));
        Assert.That(scenario.World.Smoother.LargestCorrection, Is.LessThan(RenderSmoother.TeleportThreshold));
        Assert.That(scenario.World.Smoother.Snaps, Is.EqualTo(0));
        scenario.AssertConverged();
    }

    [Test]
    public void ReorderedSnapshots_TheOlderOneIsIgnored()
    {
        Scenario scenario = new Scenario(2, 2, (tick, isUplink) => true)
        {
            SwapSnapshotsEvery = 5,
        };

        scenario.Run();

        Assert.That(scenario.World.StaleSnapshots, Is.GreaterThan(0));
        Assert.That(scenario.World.Smoother.LargestCorrection, Is.LessThan(1e-4f));
        scenario.AssertConverged();
    }

    [Test]
    public void ReorderedAndLostInputs_StillConverge()
    {
        Scenario scenario = new Scenario(3, 1, (tick, isUplink) => !isUplink || tick % 7 != 0)
        {
            SwapInputsEvery = 4,
        };

        scenario.Run();

        Assert.That(scenario.Server.StaleInputs, Is.GreaterThan(0), "an overtaken input reached the server late");
        Assert.That(scenario.World.Smoother.Snaps, Is.EqualTo(0));
        scenario.AssertConverged();
    }

    [Test]
    public void LostStop_IsRepeatedUntilTheServerAcknowledgesIt()
    {
        int stopTick = MoveTicks + 1;
        Scenario scenario = new Scenario(1, 1, (tick, isUplink) => !isUplink || tick < stopTick || tick > stopTick + 2);

        scenario.Run();

        scenario.AssertConverged();
        Assert.That(scenario.IntentsSentWhileIdle, Is.GreaterThan(3), "the stop was sent again after it was lost");
    }

    [Test]
    public void StopsLostAfterTheAcknowledgedOne_DoNotLingerAsPendingInputs()
    {
        int stopTick = MoveTicks + 1;
        Scenario scenario = new Scenario(3, 3, (tick, isUplink) => !isUplink || tick <= stopTick);

        scenario.Run();

        scenario.AssertConverged();
    }

    [Test]
    public void IdlePlayer_SendsNothing()
    {
        Scenario scenario = new Scenario(1, 1, (tick, isUplink) => true)
        {
            MoveTickCount = 0,
        };

        scenario.Run();

        Assert.That(scenario.Driver.IntentsSent, Is.EqualTo(0));
    }

    [Test]
    public void WalkToAPoint_EndsWhereTheServerSaysItDoes()
    {
        Scenario scenario = new Scenario(3, 3, (tick, isUplink) => !isUplink || tick % 9 != 0)
        {
            MoveTickCount = 0,
            Destination = ClientTestGrids.Center(7, 5),
            SettleTickCount = 160,
        };

        scenario.Run();

        scenario.AssertConverged();
        // Half a step of arrival slack plus one step a late correction may still shift the body by.
        Assert.That(scenario.Server.Position.X, Is.EqualTo(7.5f).Within(0.4f));
        Assert.That(scenario.Server.Position.Z, Is.EqualTo(5.5f).Within(0.4f));
    }

    private sealed class Scenario : IMoveIntentSink
    {
        private readonly int m_uplinkDelay;
        private readonly int m_downlinkDelay;
        private readonly Func<int, bool, bool> m_delivers;
        private readonly List<KeyValuePair<int, MoveIntent>> m_uplink = new List<KeyValuePair<int, MoveIntent>>();
        private readonly List<KeyValuePair<int, EntitySnapshot>> m_downlink =
            new List<KeyValuePair<int, EntitySnapshot>>();

        private readonly MovementController m_controller;
        private int m_tick;
        private int m_sentThisRun;

        public Scenario(int uplinkDelay, int downlinkDelay, Func<int, bool, bool> delivers)
        {
            NavigationGrid grid = ClientTestGrids.CreateYard();
            m_uplinkDelay = uplinkDelay;
            m_downlinkDelay = downlinkDelay;
            m_delivers = delivers;
            World = ClientWorldFixture.Create(grid, Start);
            Server = new FakeMovementServer(grid, ClientWorldFixture.LocalEntity, Start);
            m_controller = new MovementController(grid);
            Driver = new LocalPlayerDriver(m_controller, new MoveIntentProducer(), World, this);
        }

        public ClientWorld World { get; }

        public FakeMovementServer Server { get; }

        public LocalPlayerDriver Driver { get; }

        public int MoveTickCount { get; set; } = MoveTicks;

        public int SettleTickCount { get; set; } = SettleTicks;

        public WorldPosition? Destination { get; set; }

        public int SwapSnapshotsEvery { get; set; }

        public int SwapInputsEvery { get; set; }

        public int LargestPendingCount { get; private set; }

        public int IntentsSentWhileIdle { get; private set; }

        public int IntentsSentAtTheEnd { get; private set; }

        public void Send(MoveIntent intent)
        {
            m_sentThisRun++;
            if (!m_delivers(m_tick, true))
            {
                return;
            }

            int arrival = m_tick + m_uplinkDelay;
            if (SwapInputsEvery > 0 && m_tick % SwapInputsEvery == 0)
            {
                arrival += 2;
            }

            m_uplink.Add(new KeyValuePair<int, MoveIntent>(arrival, intent));
        }

        public void Run()
        {
            if (Destination.HasValue)
            {
                Assert.That(m_controller.TryMoveTo(Start, Destination.Value), Is.True);
            }

            for (m_tick = 1; m_tick <= MoveTickCount + SettleTickCount; m_tick++)
            {
                bool isHoldingAKey = m_tick <= MoveTickCount;
                m_controller.SetManualDirection(isHoldingAKey ? 1f : 0f, isHoldingAKey ? -0.35f : 0f);

                int sentBefore = m_sentThisRun;
                Driver.Tick((uint)m_tick);
                if (!isHoldingAKey && !Destination.HasValue)
                {
                    IntentsSentWhileIdle += m_sentThisRun - sentBefore;
                }

                if (m_tick > MoveTickCount + SettleTickCount - QuietTicks)
                {
                    IntentsSentAtTheEnd += m_sentThisRun - sentBefore;
                }

                LargestPendingCount = Math.Max(LargestPendingCount, World.Predictor.PendingCount);

                Deliver(m_uplink, Server.Receive);
                EntitySnapshot snapshot = Server.Step();
                if (m_delivers(m_tick, false))
                {
                    int arrival = m_tick + m_downlinkDelay;
                    if (SwapSnapshotsEvery > 0 && m_tick % SwapSnapshotsEvery == 0)
                    {
                        arrival += 2;
                    }

                    m_downlink.Add(new KeyValuePair<int, EntitySnapshot>(arrival, snapshot));
                }

                Deliver(m_downlink, World.OnSnapshot);
                World.Advance(ClientTestGrids.TickSeconds);
            }
        }

        public void AssertConverged()
        {
            Assert.That(World.Predictor.PendingCount, Is.EqualTo(0), "every input was acknowledged");
            Assert.That(IntentsSentAtTheEnd, Is.EqualTo(0), "a player at rest goes quiet after the stop is acked");
            Assert.That(World.Predictor.Position.X, Is.EqualTo(Server.Position.X).Within(1e-3f));
            Assert.That(World.Predictor.Position.Z, Is.EqualTo(Server.Position.Z).Within(1e-3f));
            WorldPosition drawn = World.Smoother.Sample(1f);
            Assert.That(drawn.X, Is.EqualTo(Server.Position.X).Within(1e-2f));
            Assert.That(drawn.Z, Is.EqualTo(Server.Position.Z).Within(1e-2f));
        }

        private void Deliver<T>(List<KeyValuePair<int, T>> link, Action<T> receive)
        {
            for (int index = 0; index < link.Count;)
            {
                if (link[index].Key <= m_tick)
                {
                    T message = link[index].Value;
                    link.RemoveAt(index);
                    receive(message);
                }
                else
                {
                    index++;
                }
            }
        }
    }
}
}
