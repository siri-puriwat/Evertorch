using System;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
[TestFixture]
public sealed class SeededRandomSourceTests
{
    // Reference values from an independent SplitMix64 implementation; they pin the sequence across platforms.
    [Test]
    public void Next_ForKnownSeed_MatchesReferenceSequence()
    {
        SeededRandomSource random = new SeededRandomSource(12345);

        int[] values = Enumerable.Range(0, 8).Select(_ => random.Next(1000)).ToArray();

        Assert.That(values, Is.EqualTo(new[] { 133, 204, 119, 176, 506, 337, 122, 431 }));
    }

    [Test]
    public void Next_ForMaximumSeed_WrapsWithoutOverflowing()
    {
        SeededRandomSource random = new SeededRandomSource(ulong.MaxValue);

        int[] values = Enumerable.Range(0, 5).Select(_ => random.Next(100)).ToArray();

        Assert.That(values, Is.EqualTo(new[] { 89, 91, 21, 42, 70 }));
    }

    [Test]
    public void Next_ForSameSeed_RepeatsTheSequence()
    {
        SeededRandomSource first = new SeededRandomSource(7);
        SeededRandomSource second = new SeededRandomSource(7);

        for (int index = 0; index < 100; index++)
        {
            Assert.That(second.Next(int.MaxValue), Is.EqualTo(first.Next(int.MaxValue)));
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(100)]
    [TestCase(int.MaxValue)]
    public void Next_ForAnyBound_StaysBelowIt(int exclusiveMax)
    {
        SeededRandomSource random = new SeededRandomSource(99);

        for (int index = 0; index < 1000; index++)
        {
            Assert.That(random.Next(exclusiveMax), Is.InRange(0, exclusiveMax - 1));
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Next_WhenBoundIsNotPositive_Throws(int exclusiveMax)
    {
        SeededRandomSource random = new SeededRandomSource(1);
        Action draw = () => random.Next(exclusiveMax);

        Assert.That(draw, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
}
