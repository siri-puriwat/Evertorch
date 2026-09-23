using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class RemoteEntityBufferTests
{
    private static readonly WorldDirection North = new(0f, 1f);
    private static readonly WorldDirection East = new(1f, 0f);

    [Test]
    public void Add_BeyondCapacity_ForgetsTheOldest()
    {
        var buffer = new RemoteEntityBuffer();

        for (int index = 0; index < RemoteEntityBuffer.Capacity + 5; index++)
        {
            buffer.Add(index, new WorldPosition(index, 0f, 0f), North);
        }

        buffer.TrySample(0.0, out WorldPosition oldest, out WorldDirection _);
        Assert.That(buffer.Count, Is.EqualTo(RemoteEntityBuffer.Capacity));
        Assert.That(oldest.X, Is.EqualTo(5f));
    }

    [Test]
    public void Add_OlderOrDuplicateState_IsIgnored()
    {
        var buffer = new RemoteEntityBuffer();
        buffer.Add(1.05, new WorldPosition(1f, 0f, 0f), North);

        buffer.Add(1.00, new WorldPosition(9f, 0f, 0f), North);
        buffer.Add(1.05, new WorldPosition(9f, 0f, 0f), North);

        Assert.That(buffer.Count, Is.EqualTo(1));
    }

    [Test]
    public void TrySample_AcrossATeleport_JumpsInsteadOfSlidingThroughTheWorld()
    {
        var buffer = new RemoteEntityBuffer();
        buffer.Add(1.00, new WorldPosition(0f, 0f, 0f), North);
        buffer.Add(1.05, new WorldPosition(30f, 0f, 0f), North);

        buffer.TrySample(1.01, out WorldPosition position, out WorldDirection _);

        Assert.That(position.X, Is.EqualTo(30f));
    }

    [Test]
    public void TrySample_BeforeTheOldestState_HoldsIt()
    {
        var buffer = new RemoteEntityBuffer();
        buffer.Add(2.0, new WorldPosition(3f, 0f, 3f), North);

        buffer.TrySample(1.0, out WorldPosition position, out WorldDirection _);

        Assert.That(position, Is.EqualTo(new WorldPosition(3f, 0f, 3f)));
    }

    [Test]
    public void TrySample_BetweenTwoStates_BlendsPositionAndFacing()
    {
        var buffer = new RemoteEntityBuffer();
        buffer.Add(1.00, new WorldPosition(0f, 0f, 0f), North);
        buffer.Add(1.05, new WorldPosition(0.25f, 0.1f, 0f), East);

        bool found = buffer.TrySample(1.025, out WorldPosition position, out WorldDirection facing);

        Assert.That(found, Is.True);
        Assert.That(position.X, Is.EqualTo(0.125f).Within(1e-5f));
        Assert.That(position.Y, Is.EqualTo(0.05f).Within(1e-5f));
        Assert.That(facing.X, Is.EqualTo(0.7071f).Within(1e-3f));
        Assert.That(facing.Z, Is.EqualTo(0.7071f).Within(1e-3f));
    }

    [Test]
    public void TrySample_PastTheNewestState_WaitsThereInsteadOfGuessing()
    {
        var buffer = new RemoteEntityBuffer();
        buffer.Add(1.00, new WorldPosition(0f, 0f, 0f), North);
        buffer.Add(1.05, new WorldPosition(0.25f, 0f, 0f), North);

        buffer.TrySample(5.0, out WorldPosition position, out WorldDirection _);

        Assert.That(position, Is.EqualTo(new WorldPosition(0.25f, 0f, 0f)));
        Assert.That(buffer.Count, Is.EqualTo(1), "older states are released once passed");
    }

    [Test]
    public void TrySample_WhenEmpty_IsFalse()
    {
        Assert.That(new RemoteEntityBuffer().TrySample(1.0, out WorldPosition _, out WorldDirection _), Is.False);
    }
}
}
