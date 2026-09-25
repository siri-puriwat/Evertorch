using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ActionLockTests
{
    [Test]
    public void Advance_HoldsASwingForItsTicks_ThenFreesIt()
    {
        var actionLock = new ActionLock();
        actionLock.LockForSwing(2);

        bool first = actionLock.Advance();
        bool second = actionLock.Advance();
        bool third = actionLock.Advance();

        Assert.That((first, second, third), Is.EqualTo((true, true, false)));
        Assert.That(actionLock.IsSwingLocked, Is.False);
    }

    [Test]
    public void Advance_WhenOneSpanEnds_KeepsTheOtherHolding()
    {
        var actionLock = new ActionLock();
        actionLock.LockForSwing(1);
        actionLock.LockForCast(3);

        bool[] ticks = { actionLock.Advance(), actionLock.Advance(), actionLock.Advance(), actionLock.Advance() };

        Assert.That(ticks, Is.EqualTo(new[] { true, true, true, false }), "the swing's end does not free the cast");
    }

    [Test]
    public void EndCast_FreesTheCastAtOnce_ButNotASwing()
    {
        var actionLock = new ActionLock();
        actionLock.LockForCast(10);
        actionLock.LockForSwing(2);

        actionLock.EndCast();

        Assert.That(actionLock.IsCastLocked, Is.False);
        Assert.That(actionLock.Advance(), Is.True, "the swing still holds");
        Assert.That(actionLock.Advance(), Is.True);
        Assert.That(actionLock.Advance(), Is.False);
    }

    [Test]
    public void LockForSwing_WithNoTicks_HoldsNothing()
    {
        var actionLock = new ActionLock();

        actionLock.LockForSwing(0);
        actionLock.LockForCast(-3);

        Assert.That(actionLock.Advance(), Is.False);
    }
}
}
