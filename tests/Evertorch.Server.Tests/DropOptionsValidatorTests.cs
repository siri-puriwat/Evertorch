using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The range of the cards' drop scale (System Architecture §12).
/// </summary>
[TestFixture]
public sealed class DropOptionsValidatorTests
{
    [TestCase(0)]
    [TestCase(1001)]
    public void Drops_WithAScaleOutOfRange_FailsNamingTheKey(int scale)
    {
        ValidateOptionsResult result =
            new DropOptionsValidator().Validate(null, new DropOptions { CardChanceScale = scale });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("Drops:CardChanceScale"));
    }

    [TestCase(1)]
    [TestCase(1000)]
    public void Drops_WithAScaleInRange_Passes(int scale)
    {
        Assert.That(
            new DropOptionsValidator().Validate(null, new DropOptions { CardChanceScale = scale }).Succeeded,
            Is.True);
    }

    [Test]
    public void Drops_ByDefault_KeepTheCardsChances()
    {
        Assert.That(new DropOptions().CardChanceScale, Is.EqualTo(1));
    }
}
}
