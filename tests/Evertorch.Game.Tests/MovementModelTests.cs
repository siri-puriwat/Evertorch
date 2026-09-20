using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class MovementModelTests
{
    [TestCase(1f, 0f, 1f, 0f)]
    [TestCase(0f, -1f, 0f, -1f)]
    [TestCase(0.3f, 0f, 1f, 0f)]
    [TestCase(0f, 250f, 0f, 1f)]
    [TestCase(-3f, 4f, -0.6f, 0.8f)]
    public void NormalizeOrZero_ForFiniteDirection_ReturnsUnitVectorWhateverTheRequestedLength(
        float x,
        float z,
        float expectedX,
        float expectedZ)
    {
        WorldDirection direction = MovementModel.NormalizeOrZero(x, z);

        Assert.That(direction.X, Is.EqualTo(expectedX).Within(1e-6f));
        Assert.That(direction.Z, Is.EqualTo(expectedZ).Within(1e-6f));
    }

    [TestCase(1f, 1f)]
    [TestCase(-1f, 1f)]
    [TestCase(0.1f, -0.1f)]
    [TestCase(1e30f, 1e30f)]
    [TestCase(3.4e38f, -3.4e38f)]
    public void NormalizeOrZero_ForDiagonal_HasUnitLength(float x, float z)
    {
        WorldDirection direction = MovementModel.NormalizeOrZero(x, z);

        double length = Math.Sqrt(((double)direction.X * direction.X) + ((double)direction.Z * direction.Z));
        Assert.That(length, Is.EqualTo(1d).Within(1e-6));
    }

    [TestCase(0f, 0f)]
    [TestCase(0.0005f, 0.0005f)]
    [TestCase(-0.0009f, 0f)]
    public void NormalizeOrZero_BelowMinimumMagnitude_ReturnsZero(float x, float z)
    {
        Assert.That(MovementModel.NormalizeOrZero(x, z), Is.EqualTo(new WorldDirection(0f, 0f)));
    }

    [TestCase(float.NaN, 1f)]
    [TestCase(1f, float.NaN)]
    [TestCase(float.PositiveInfinity, 0f)]
    [TestCase(0f, float.NegativeInfinity)]
    [TestCase(float.PositiveInfinity, float.NegativeInfinity)]
    public void NormalizeOrZero_ForNonFiniteInput_ReturnsZero(float x, float z)
    {
        Assert.That(MovementModel.NormalizeOrZero(x, z), Is.EqualTo(new WorldDirection(0f, 0f)));
    }
}
}
