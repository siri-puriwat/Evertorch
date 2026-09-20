using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class SharedEntityIdTests
{
    [Test]
    public void Value_AfterConstruction_ReturnsSuppliedValue()
    {
        EntityId entityId = new EntityId(long.MinValue);

        Assert.That(entityId.Value, Is.EqualTo(long.MinValue));
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        EntityId left = new EntityId(7);
        EntityId right = new EntityId(7);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        Assert.That(left != new EntityId(8), Is.True);
    }
}
}
