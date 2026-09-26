using System;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Where the skill bar, the feedback lines, and the windows sit (Prototype Content §2, owner decision 10), in canvas
///     units, which are 1,080 wide on every screen: the lines clear the bar, neither covers the stick or the touch
///     buttons, and no window row lies over a control.
/// </summary>
[TestFixture]
public sealed class PanelLayoutTests
{
    // Three lines of 24-point text with the panel's padding and spacing.
    private const float LinesHeight = 124f;

    [TestCase(607.5f, TestName = "Landscape 1920 x 1080")]
    [TestCase(1920f, TestName = "Portrait 1080 x 1920")]
    public void FeedbackLines_SitAboutAFifthUp_RaisedToClearTheSkillBar(float canvasHeight)
    {
        float bottom = FeedbackLines.BottomFor(canvasHeight);

        Assert.That(bottom, Is.EqualTo(Math.Max(0.2f * canvasHeight, SkillBar.Top + 16f)).Within(1e-3f));
        Assert.That(bottom - SkillBar.Top, Is.GreaterThanOrEqualTo(16f));
        Assert.That(bottom + LinesHeight, Is.LessThan(canvasHeight * 0.6f), "the lines stay in the lower part");
    }

    [TestCase(486f, TestName = "Landscape 20:9")]
    [TestCase(607.5f, TestName = "Landscape 16:9")]
    [TestCase(1920f, TestName = "Portrait 9:16")]
    public void InventoryWindow_HangsBelowTheStatusBar_AndStopsAboveTheTouchButtonsWhileTheyShow(float canvasHeight)
    {
        Rect column = TouchControls.ButtonColumnBounds(ClientUI.CanvasWidth);
        Rect withTouch = InventoryWindow.BoundsFor(canvasHeight, 60, true);
        Rect withoutTouch = InventoryWindow.BoundsFor(canvasHeight, 60, false);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(column), Is.False, "no row under a touch button");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "nor under the stick");
        Assert.That(withoutTouch.yMin, Is.EqualTo(8f).Within(1e-3f), "without them, down to the bottom margin");
    }

    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void SkillBarAndFeedbackLines_NeverCoverTheStickOrTheTouchButtons(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        Rect bar = SkillBar.BoundsFor(width, SkillSlots.Count);
        var lines = new Rect(
            (width - FeedbackLines.Width) / 2f,
            FeedbackLines.BottomFor(canvasHeight),
            FeedbackLines.Width,
            LinesHeight);
        Rect stick = TouchControls.StickBounds;
        Rect column = TouchControls.ButtonColumnBounds(width);

        Assert.That(bar.Overlaps(stick), Is.False, "the bar and the stick");
        Assert.That(bar.Overlaps(column), Is.False, "the bar and the touch buttons");
        Assert.That(lines.Overlaps(bar), Is.False, "the lines and the bar");
        Assert.That(lines.Overlaps(stick), Is.False, "the lines and the stick");
        Assert.That(lines.Overlaps(column), Is.False, "the lines and the touch buttons");
        Assert.That(bar.xMin, Is.GreaterThan(stick.xMax));
        Assert.That(bar.xMax, Is.LessThan(column.xMin));
    }

    // A row over a skill slot or the stick would take the presses meant for it, and a press of a row buys or sells.
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void NpcWindow_HangsBelowTheStatusBar_AndStopsAboveTheSkillBarAndTheStick(float canvasHeight)
    {
        Rect bar = SkillBar.BoundsFor(ClientUI.CanvasWidth, SkillSlots.Count);
        Rect withTouch = NpcWindow.BoundsFor(canvasHeight, 100, true);
        Rect withoutTouch = NpcWindow.BoundsFor(canvasHeight, 100, false);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "no row over the stick");
        Assert.That(withTouch.Overlaps(bar), Is.False, "nor over the skill bar");
        Assert.That(withoutTouch.yMin, Is.EqualTo(SkillBar.Top + 8f).Within(1e-3f), "without the stick, to the bar");
        Assert.That(withTouch.height, Is.GreaterThan(92f + 2 * 30f + 6f), "two rows show on the shortest screen");
    }

    [Test]
    public void InventoryWindow_WhileItsRowsFit_IsJustTallEnoughForThem()
    {
        Rect three = InventoryWindow.BoundsFor(1920f, 3, true);

        Assert.That(three.height, Is.EqualTo(54f + 3 * 30f + 2 * 4f).Within(1e-3f));
    }

    [Test]
    public void NpcWindow_WhileItsRowsFit_IsJustTallEnoughForThem()
    {
        Rect three = NpcWindow.BoundsFor(1920f, 3, true);

        Assert.That(three.height, Is.EqualTo(92f + 3 * 30f + 2 * 6f).Within(1e-3f));
    }
}
}
