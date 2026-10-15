using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The range of the ailments' chance scale (System Architecture §12).
/// </summary>
[TestFixture]
public sealed class CombatOptionsValidatorTests
{
    [TestCase(0)]
    [TestCase(101)]
    public void Combat_WithAScaleOutOfRange_FailsNamingTheKey(int scale)
    {
        ValidateOptionsResult result =
            new CombatOptionsValidator().Validate(null, new CombatOptions { AilmentChanceScale = scale });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("Combat:AilmentChanceScale"));
    }

    [TestCase(1)]
    [TestCase(100)]
    public void Combat_WithAScaleInRange_Passes(int scale)
    {
        Assert.That(
            new CombatOptionsValidator().Validate(null, new CombatOptions { AilmentChanceScale = scale }).Succeeded,
            Is.True);
    }

    [Test]
    public void Combat_ByDefault_KeepsTheAilmentsChances()
    {
        Assert.That(new CombatOptions().AilmentChanceScale, Is.EqualTo(1));
    }
}
}
