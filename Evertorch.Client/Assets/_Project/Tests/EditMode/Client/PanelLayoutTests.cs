using System;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Where the skill bar and the feedback lines sit (Prototype Content §2, owner decision 10), in canvas units, which
///     are 1,080 wide on every screen: the lines clear the bar, and neither covers the stick or the touch buttons.
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
}
}
