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
    public void AwaitCast_HoldsBothFreeChecks_UntilItsCastIsHeard_Refused_OrItsTicksPass()
    {
        var heard = new ActionLock();
        var refused = new ActionLock();
        var lost = new ActionLock();
        foreach (ActionLock actionLock in new[] { heard, refused, lost })
        {
            actionLock.AwaitCast(3);
        }

        bool[] whileAwaited = { heard.HasBeenFreeFor(0), heard.HasBeenFreeOfCastFor(0) };
        heard.LockForCast(0);
        refused.StopAwaitingCast();
        bool[] lostEarly = new bool[3];
        for (int tick = 0; tick < lostEarly.Length; tick++)
        {
            lostEarly[tick] = lost.HasBeenFreeOfCastFor(0);
            lost.Advance();
        }

        Assert.That(whileAwaited, Is.EqualTo(new[] { false, false }), "a cast asked for may be under way");
        Assert.That(
            (heard.HasBeenFreeFor(ActionLock.ClearTicks), heard.HasBeenFreeOfCastFor(ActionLock.ClearTicks)),
            Is.EqualTo((true, true)),
            "a cast of 0 ms was heard");
        Assert.That(refused.HasBeenFreeOfCastFor(ActionLock.ClearTicks), Is.True, "refused");
        Assert.That(lostEarly, Is.EqualTo(new[] { false, false, false }));
        Assert.That(lost.HasBeenFreeOfCastFor(ActionLock.ClearTicks), Is.True, "no word within its ticks");
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
    public void HasBeenFreeFor_CountsTheTicksSinceTheLastHeldOne()
    {
        var actionLock = new ActionLock();
        bool freshWorld = actionLock.HasBeenFreeFor(2);
        actionLock.LockForSwing(1);
        bool whileHeld = actionLock.HasBeenFreeFor(0);

        bool[] free = new bool[3];
        for (int tick = 0; tick < free.Length; tick++)
        {
            actionLock.Advance();
            free[tick] = actionLock.HasBeenFreeFor(2);
        }

        Assert.That(freshWorld, Is.True, "nothing has held a new world's player");
        Assert.That(whileHeld, Is.False);
        Assert.That(free, Is.EqualTo(new[] { false, false, true }), "the held tick, then two free ones");
    }

    [Test]
    public void HasBeenFreeOfCastFor_CountsTheTicksSinceTheCastLastHeld_WhateverTheSwingDoes()
    {
        var actionLock = new ActionLock();
        bool freshWorld = actionLock.HasBeenFreeOfCastFor(ActionLock.ClearTicks);
        actionLock.LockForSwing(3);
        actionLock.Advance();
        bool duringASwing = actionLock.HasBeenFreeOfCastFor(ActionLock.ClearTicks);
        actionLock.LockForCast(1);
        bool whileCasting = actionLock.HasBeenFreeOfCastFor(0);

        bool[] free = new bool[3];
        for (int tick = 0; tick < free.Length; tick++)
        {
            actionLock.Advance();
            free[tick] = actionLock.HasBeenFreeOfCastFor(ActionLock.ClearTicks);
        }

        Assert.That(freshWorld, Is.True, "no cast has held a new world's player");
        Assert.That(duringASwing, Is.True, "a swing is no cast");
        Assert.That(whileCasting, Is.False);
        Assert.That(free, Is.EqualTo(new[] { false, false, true }), "the held tick, then two free ones");
    }

    [Test]
    public void IsSwingDueWithin_CountsDownToTheNextSwing_WithoutHoldingThePlayer()
    {
        var actionLock = new ActionLock();
        actionLock.LockForSwing(1, 4);

        bool[] due = new bool[5];
        bool[] held = new bool[5];
        for (int tick = 0; tick < due.Length; tick++)
        {
            due[tick] = actionLock.IsSwingDueWithin(2);
            held[tick] = actionLock.Advance();
        }

        Assert.That(due, Is.EqualTo(new[] { false, false, true, true, false }));
        Assert.That(held, Is.EqualTo(new[] { true, false, false, false, false }));
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
