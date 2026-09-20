using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
[TestFixture]
public sealed class RenewalMovementRulesTests
{
    [TestCase(5f, 5f)]
    [TestCase(0f, 0f)]
    [TestCase(-3f, 0f)]
    [TestCase(20f, 20f)]
    [TestCase(250f, 20f)]
    [TestCase(float.PositiveInfinity, 20f)]
    [TestCase(float.NaN, 0f)]
    public void CalculateMovement_ForAnyBaseSpeed_StaysWithinBounds(float baseSpeed, float expected)
    {
        MovementParameters parameters = new RenewalMovementRules().CalculateMovement(new MovementContext(baseSpeed));

        Assert.That(parameters.Speed, Is.EqualTo(expected));
    }
}
}
