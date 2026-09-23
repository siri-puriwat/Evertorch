using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A server process that ends without a controlled shutdown loses only what its last checkpoint did not cover
///     (Persistence §6, §11: "Checkpointed position is restored after simulated process loss"). Two server pipelines
///     share one real PostgreSQL 18 database; the first is simply abandoned.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ProcessLossTests
{
    private PostgresFixture m_database = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private TestServer NewServer()
    {
        return new TestServer(
            persistence: new PersistenceOptions { CheckpointIntervalMs = 1000 },
            store: new PostgresGameStore(m_database.ConnectionString));
    }

    [Test]
    public void Enter_AfterTheServerProcessWasLost_RestoresTheLastCheckpointedPosition()
    {
        TestServer lost = NewServer();
        ConnectionId first = lost.EnterWorldAs("loss", "LossTest");
        lost.Place(first, 3.5f, 4.5f);
        WorldPosition checkpointed = lost.PlayerOf(first).Position;
        lost.Tick(TestServer.TickRate + 1);
        lost.Persistence.RunUntilIdle();
        lost.Place(first, 5.5f, 4.5f);

        TestServer restarted = NewServer();
        ConnectionId again = restarted.EnterWorldAs("loss", "LossTest");

        Assert.That(restarted.PlayerOf(again).Position, Is.EqualTo(checkpointed));
    }
}
}
