using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class PlayerInputQueueTests
{
    [TestCase(5u, 5u)]
    [TestCase(5u, 4u)]
    [TestCase(5u, 0u)]
    [TestCase(0u, uint.MaxValue)]
    public void TryEnqueue_WithSequenceNotNewerThanTheLast_IsRefusedAndCounted(uint first, uint second)
    {
        var queue = new PlayerInputQueue(4);
        queue.TryEnqueue(Intent(first));

        bool isAccepted = queue.TryEnqueue(Intent(second));

        Assert.That(isAccepted, Is.False);
        Assert.That(queue.Stale, Is.EqualTo(1));
        Assert.That(queue.Count, Is.EqualTo(1));
    }

    private static MoveIntent Intent(uint sequence)
    {
        return new MoveIntent(sequence, sequence, 1f, 0f);
    }

    [Test]
    public void Constructor_WithoutCapacity_Throws()
    {
        Action create = () => _ = new PlayerInputQueue(0);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void TryDequeue_ReturnsInputsInSequenceOrder()
    {
        var queue = new PlayerInputQueue(4);
        queue.TryEnqueue(Intent(1));
        queue.TryEnqueue(Intent(2));
        queue.TryEnqueue(Intent(5));

        uint[] sequences = new uint[3];
        for (int index = 0; index < sequences.Length; index++)
        {
            queue.TryDequeue(out MoveIntent intent);
            sequences[index] = intent.Sequence;
        }

        Assert.That(sequences, Is.EqualTo(new uint[] { 1, 2, 5 }));
        Assert.That(queue.TryDequeue(out _), Is.False);
    }

    [Test]
    public void TryEnqueue_AcrossTheSequenceWrap_AcceptsTheNewerInput()
    {
        var queue = new PlayerInputQueue(4);
        queue.TryEnqueue(Intent(uint.MaxValue));

        Assert.That(queue.TryEnqueue(Intent(0)), Is.True);
        Assert.That(queue.TryEnqueue(Intent(1)), Is.True);
    }

    [Test]
    public void TryEnqueue_AfterTheNewerInputWasConsumed_StillRefusesTheOlderOne()
    {
        var queue = new PlayerInputQueue(4);
        queue.TryEnqueue(Intent(9));
        queue.TryDequeue(out _);

        Assert.That(queue.TryEnqueue(Intent(8)), Is.False);
    }

    [Test]
    public void TryEnqueue_WhenFull_DropsTheOldestAndKeepsOrder()
    {
        var queue = new PlayerInputQueue(3);
        for (uint sequence = 1; sequence <= 5; sequence++)
        {
            queue.TryEnqueue(Intent(sequence));
        }

        uint[] remaining = new uint[3];
        for (int index = 0; index < remaining.Length; index++)
        {
            queue.TryDequeue(out MoveIntent intent);
            remaining[index] = intent.Sequence;
        }

        Assert.That(queue.Dropped, Is.EqualTo(2));
        Assert.That(remaining, Is.EqualTo(new uint[] { 3, 4, 5 }));
    }
}
}
